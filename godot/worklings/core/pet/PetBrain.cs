using System.Collections.Generic;
using Worklings.Core.Progression;

namespace Worklings.Core.Pet;

/// How fast a Workling's needs move on their own.
///
/// Ported from Sources/CompanionCore/PetBrain.swift.
public sealed class PetSimulationRates
{
    public double HungerPerHour { get; }
    public double EnergyPerHour { get; }
    public double HappinessPerHour { get; }
    public double MaximumOfflineHours { get; }
    public double WorkingHungerMultiplier { get; }
    public double WorkingEnergyMultiplier { get; }
    public double AwayTrustPerHour { get; }
    public double AwayGracePeriodHours { get; }
    public double LongAwayTrustPerHour { get; }
    public double WorkLogCooldownMinutes { get; }
    public int WorkLogDailyCap { get; }
    public double WorkLogHappinessGain { get; }

    public PetSimulationRates(
        double hungerPerHour = 4,
        double energyPerHour = 3,
        double happinessPerHour = 1,
        double maximumOfflineHours = 24 * 7,
        double workingHungerMultiplier = 1.25,
        double workingEnergyMultiplier = 1.3,
        double awayTrustPerHour = 2,
        double awayGracePeriodHours = 1,
        double longAwayTrustPerHour = 0.2,
        double workLogCooldownMinutes = 30,
        int workLogDailyCap = 6,
        double workLogHappinessGain = 3)
    {
        HungerPerHour = System.Math.Max(hungerPerHour, 0);
        EnergyPerHour = System.Math.Max(energyPerHour, 0);
        HappinessPerHour = System.Math.Max(happinessPerHour, 0);
        MaximumOfflineHours = System.Math.Max(maximumOfflineHours, 0);
        WorkingHungerMultiplier = System.Math.Max(workingHungerMultiplier, 0);
        WorkingEnergyMultiplier = System.Math.Max(workingEnergyMultiplier, 0);
        AwayTrustPerHour = System.Math.Max(awayTrustPerHour, 0);
        AwayGracePeriodHours = System.Math.Max(awayGracePeriodHours, 0);
        LongAwayTrustPerHour = System.Math.Max(longAwayTrustPerHour, 0);
        WorkLogCooldownMinutes = System.Math.Max(workLogCooldownMinutes, 0);
        WorkLogDailyCap = System.Math.Max(workLogDailyCap, 0);
        WorkLogHappinessGain = System.Math.Max(workLogHappinessGain, 0);
    }
}

/// The simulation: needs decaying over time, and what a care action does to
/// them.
///
/// Ported from the care half of Sources/CompanionCore/PetBrain.swift —
/// `advance`, `perform`, `grantingXP` and `updatedState`. **The activity half is
/// not here**: `observe`, `workLogAvailability` and the response machinery all
/// take an `ActivityEvent` and an `ActivityContext`, and neither type is ported.
/// That is the activity pipeline, which is a slice of its own and the only part
/// that must also be re-authored per platform.
///
/// What this means in practice: the pet gets hungry, can be fed, played with,
/// petted and put to sleep, and earns XP for it. It does not yet notice you
/// working.
public sealed class PetBrain
{
    public PetSimulationRates Rates { get; }
    public PetProgressionRates ProgressionRates { get; }

    public PetBrain(
        PetSimulationRates? rates = null,
        PetProgressionRates? progressionRates = null)
    {
        Rates = rates ?? new PetSimulationRates();
        ProgressionRates = progressionRates ?? new PetProgressionRates();
    }

    /// Moves needs forward to `now`. Time is the only input — this is what makes
    /// a pet you left alone overnight hungry when you come back.
    ///
    /// Capped at MaximumOfflineHours so a pet abandoned for a month is not
    /// unrecoverable, and clamped to nothing at all when the elapsed time is
    /// zero or negative, which a clock change can produce.
    ///
    /// The `context` parameter of the Swift original is omitted: it only selects
    /// the working multipliers and the away-trust drain, and both come from the
    /// unported activity pipeline. Behaviour here matches Swift's `.quiet`.
    public PetState Advance(PetState state, System.DateTimeOffset now)
    {
        double elapsedSeconds = (now - state.LastUpdatedAt).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return state;
        }

        double elapsedHours = System.Math.Min(elapsedSeconds / 3600, Rates.MaximumOfflineHours);
        double hunger = state.Needs.Hunger + Rates.HungerPerHour * elapsedHours;
        double energy = state.Needs.Energy - Rates.EnergyPerHour * elapsedHours;

        // Distress compounds: a hungry, exhausted pet loses happiness and trust
        // faster than a merely bored one. Both terms are zero until their
        // threshold is crossed, so ordinary decay stays linear.
        double hungerPenalty = System.Math.Max(hunger - 75, 0) / 25;
        double exhaustionPenalty = System.Math.Max(20 - energy, 0) / 20;
        double distress = hungerPenalty + exhaustionPenalty;

        double happiness = state.Needs.Happiness
            - Rates.HappinessPerHour * elapsedHours
            - distress * 0.75 * elapsedHours;
        double trust = state.Needs.Trust - distress * 0.2 * elapsedHours;

        return UpdatedState(
            state, new PetNeeds(hunger, energy, happiness, trust), now);
    }

    /// Performs a care action, advancing needs to `now` first so the action lands
    /// on the pet's current condition rather than on whatever it was when the app
    /// last looked.
    public PetInteractionResult Perform(
        PetAction action, PetState state, System.DateTimeOffset now)
    {
        var current = Advance(state, now);
        var needs = current.Needs;

        switch (action.Kind)
        {
            case PetActionKind.Feed:
            {
                bool favourite = action.Food == current.Preferences.FavouriteFood;
                return Result(current,
                    hunger: needs.Hunger - (favourite ? 30 : 20),
                    energy: needs.Energy,
                    happiness: needs.Happiness + (favourite ? 8 : 3),
                    trust: needs.Trust + (favourite ? 3 : 1),
                    now: now,
                    reaction: favourite ? PetReaction.LovedFood : PetReaction.LikedFood);
            }

            case PetActionKind.Play:
            {
                // Too tired to play is a real answer, not a failure: the state
                // comes back advanced but otherwise untouched, so refusing costs
                // the player nothing except the play.
                if (needs.Energy < 15)
                {
                    return new PetInteractionResult(current, PetReaction.TooTiredToPlay);
                }

                bool favourite = action.Play == current.Preferences.FavouritePlayActivity;
                return Result(current,
                    hunger: needs.Hunger + (favourite ? 8 : 7),
                    energy: needs.Energy - (favourite ? 14 : 12),
                    happiness: needs.Happiness + (favourite ? 22 : 14),
                    trust: needs.Trust + (favourite ? 6 : 3),
                    now: now,
                    reaction: favourite ? PetReaction.LovedPlay : PetReaction.EnjoyedPlay);
            }

            case PetActionKind.Pet:
                return Result(current,
                    hunger: needs.Hunger,
                    energy: needs.Energy,
                    happiness: needs.Happiness + 8,
                    trust: needs.Trust + 4,
                    now: now,
                    reaction: PetReaction.Comforted);

            default:
                return Result(current,
                    hunger: needs.Hunger + 6,
                    energy: needs.Energy + 35,
                    happiness: needs.Happiness + 2,
                    trust: needs.Trust,
                    now: now,
                    reaction: PetReaction.Rested);
        }
    }

    private PetInteractionResult Result(
        PetState state,
        double hunger, double energy, double happiness, double trust,
        System.DateTimeOffset now,
        PetReaction reaction)
    {
        var updated = UpdatedState(
            state, new PetNeeds(hunger, energy, happiness, trust), now);
        return new PetInteractionResult(
            // The condition passed is the needs from BEFORE the action improved
            // them, so an action's own boost can never inflate the multiplier
            // that prices it.
            GrantingXP(ProgressionRates.CareActionXP, XPSource.Care, updated, now,
                       condition: state.Needs),
            reaction);
    }

    /// Grants XP subject to the condition multiplier, a per-source daily cap and
    /// an overall daily cap — the actual fairness mechanism. Crossing a level
    /// threshold applies that many levels' worth of class-weighted growth in the
    /// same step, so one large grant can never skip an intermediate level's
    /// stats.
    public PetState GrantingXP(
        double rawAmount,
        XPSource source,
        PetState state,
        System.DateTimeOffset now,
        System.DateTimeOffset? day = null,
        PetNeeds? condition = null)
    {
        if (rawAmount <= 0)
        {
            return state;
        }

        var chargedDay = day ?? now;
        var dailyXPBySource = state.DailyXP.Current(chargedDay, new Dictionary<string, double>());
        var dailyCountBySource =
            state.DailyEventCount.Current(chargedDay, new Dictionary<string, int>());

        string key = source.RawValue();
        double grantedTodayForSource = dailyXPBySource.TryGetValue(key, out var g) ? g : 0;
        double grantedTodayOverall = 0;
        foreach (var value in dailyXPBySource.Values) grantedTodayOverall += value;
        int countTodayForSource = dailyCountBySource.TryGetValue(key, out var c) ? c : 0;

        double sourceHeadroom =
            System.Math.Max(0, ProgressionRates.DailyCap(source) - grantedTodayForSource);
        double overallHeadroom =
            System.Math.Max(0, ProgressionRates.OverallDailyCap - grantedTodayOverall);

        // Per-event diminishing returns: the Nth grant from this source today is
        // worth factor^(N-1) of its base. Flat for every source but milestone,
        // so a no-op elsewhere.
        double decay = System.Math.Pow(ProgressionRates.DecayFactor(source), countTodayForSource);
        double multiplier =
            (condition ?? state.Needs).XPMultiplier(ProgressionRates.ConditionMultiplierFloor);
        double amount = System.Math.Min(
            rawAmount * decay * multiplier, System.Math.Min(sourceHeadroom, overallHeadroom));
        if (amount <= 0)
        {
            return state;
        }

        var updatedXP = new Dictionary<string, double>(dailyXPBySource)
        {
            [key] = grantedTodayForSource + amount,
        };
        var updatedCount = new Dictionary<string, int>(dailyCountBySource)
        {
            [key] = countTodayForSource + 1,
        };

        double newTotalXP = state.TotalXP + amount;
        int levelsGained = PetProgressionCurve.Level(newTotalXP)
            - PetProgressionCurve.Level(state.TotalXP);

        var stats = state.Stats;
        if (levelsGained > 0)
        {
            var signature = state.PetClass.SignatureStat();
            for (int i = 0; i < levelsGained; i++)
            {
                stats = stats.Growing(
                    signature,
                    ProgressionRates.SignatureStatGainPerLevel,
                    ProgressionRates.OtherStatGainPerLevel);
            }
        }

        return UpdatedState(
            state, state.Needs, now,
            totalXP: newTotalXP,
            stats: stats,
            dailyXP: new DailyTally<Dictionary<string, double>>(updatedXP, chargedDay),
            dailyEventCount: new DailyTally<Dictionary<string, int>>(updatedCount, chargedDay));
    }

    /// Delegated to PetState.Advanced rather than rebuilt field by field: a
    /// `new PetState(...)` here silently *resets* every field the brain does not
    /// know about, gear included, to the constructor defaults. That is exactly
    /// how every item won in a delve was handed back on the next needs tick.
    private PetState UpdatedState(
        PetState state,
        PetNeeds needs,
        System.DateTimeOffset now,
        double? totalXP = null,
        PetStats? stats = null,
        DailyTally<Dictionary<string, double>>? dailyXP = null,
        DailyTally<Dictionary<string, int>>? dailyEventCount = null) =>
        state.Advanced(
            needs: needs,
            at: now,
            totalXP: totalXP,
            stats: stats,
            dailyXP: dailyXP,
            dailyEventCount: dailyEventCount);
}
