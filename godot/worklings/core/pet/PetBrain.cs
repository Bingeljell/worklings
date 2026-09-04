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

    /// Multiplies every per-hour rate by `factor`, so a real-time wait during
    /// manual testing can stand in for hours without touching event deltas or
    /// production tuning. The grace period and the Log Work cooldown are DIVIDED
    /// by the same factor so every gated tier stays reachable inside a short
    /// test; the daily cap and the per-log gain are flat amounts rather than
    /// rates, so they are left alone.
    public PetSimulationRates Scaled(double factor) => new(
        hungerPerHour: HungerPerHour * factor,
        energyPerHour: EnergyPerHour * factor,
        happinessPerHour: HappinessPerHour * factor,
        maximumOfflineHours: MaximumOfflineHours,
        workingHungerMultiplier: WorkingHungerMultiplier,
        workingEnergyMultiplier: WorkingEnergyMultiplier,
        awayTrustPerHour: AwayTrustPerHour * factor,
        awayGracePeriodHours: factor > 0 ? AwayGracePeriodHours / factor : AwayGracePeriodHours,
        longAwayTrustPerHour: LongAwayTrustPerHour * factor,
        workLogCooldownMinutes: factor > 0 ? WorkLogCooldownMinutes / factor : WorkLogCooldownMinutes,
        workLogDailyCap: WorkLogDailyCap,
        workLogHappinessGain: WorkLogHappinessGain);
}

/// The simulation: needs decaying over time, what a care action does to them,
/// and what the pet makes of what it sees you doing.
///
/// Ported whole from Sources/CompanionCore/PetBrain.swift — the care half
/// (`advance`, `perform`, `grantingXP`, `updatedState`) and the activity half
/// (`observe`, `workLogAvailability` and the response machinery) alike.
///
/// What this means in practice: the pet gets hungry, can be fed, played with,
/// petted and put to sleep, earns XP for all of it, and reacts to work
/// starting, a task landing, a milestone shipping and you walking away. What is
/// still missing is anything to *deliver* those events — that is `ActivityInbox`
/// and `ActivitySources`, the only part of the pipeline that must also be
/// re-authored per platform.
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
    /// `context` selects the working multipliers and the away-trust drain. It
    /// defaults to `Quiet`, which is exactly the behaviour this had before the
    /// activity half existed — a pet that nobody is watching work.
    public PetState Advance(
        PetState state, System.DateTimeOffset now, ActivityContext? context = null)
    {
        context ??= ActivityContext.Quiet;
        double elapsedSeconds = (now - state.LastUpdatedAt).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return state;
        }

        double elapsedHours = System.Math.Min(elapsedSeconds / 3600, Rates.MaximumOfflineHours);
        // Working is hungrier and more tiring than idling. The pet is keeping you
        // company at the desk, not asleep under it.
        double hungerMultiplier = context.IsWorking ? Rates.WorkingHungerMultiplier : 1;
        double energyMultiplier = context.IsWorking ? Rates.WorkingEnergyMultiplier : 1;
        double hunger = state.Needs.Hunger + Rates.HungerPerHour * hungerMultiplier * elapsedHours;
        double energy = state.Needs.Energy - Rates.EnergyPerHour * energyMultiplier * elapsedHours;

        // Distress compounds: a hungry, exhausted pet loses happiness and trust
        // faster than a merely bored one. Both terms are zero until their
        // threshold is crossed, so ordinary decay stays linear.
        double hungerPenalty = System.Math.Max(hunger - 75, 0) / 25;
        double exhaustionPenalty = System.Math.Max(20 - energy, 0) / 20;
        double distress = hungerPenalty + exhaustionPenalty;

        double happiness = state.Needs.Happiness
            - Rates.HappinessPerHour * elapsedHours
            - distress * 0.75 * elapsedHours;
        double awayTrustDrain = AwayTrustRate(context, now) * elapsedHours;
        double trust = state.Needs.Trust - distress * 0.2 * elapsedHours - awayTrustDrain;

        return UpdatedState(
            state, new PetNeeds(hunger, energy, happiness, trust), now);
    }

    /// The two-tier away rate: full strength for a short absence, tapering to a
    /// trickle beyond the grace period, so an evening or a weekend away costs far
    /// less than the same hours would at the short-absence rate.
    ///
    /// Applies the single rate reached BY `now` across the whole tick, which is
    /// an approximation. It matters only for one unusually large gap — the Mac
    /// slept through the tier boundary — and at the normal one-tick-per-minute
    /// cadence the tiers land where they should.
    private double AwayTrustRate(ActivityContext context, System.DateTimeOffset now)
    {
        if (context.IsUserPresent)
        {
            return 0;
        }
        double awayHours =
            System.Math.Max(0, (now - (context.AwaySince ?? now)).TotalSeconds) / 3600;
        return awayHours > Rates.AwayGracePeriodHours
            ? Rates.LongAwayTrustPerHour
            : Rates.AwayTrustPerHour;
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

    /// Applies an observed activity event to the pet.
    ///
    /// Structural events shape the activity context only and cost the pet
    /// nothing; moments worth sharing move needs slightly and come back with a
    /// visible reaction. Every one of these numbers is alpha tuning.
    ///
    /// `context` is the context from *before* this event was reduced into it.
    /// That is not an oversight: `WorkEnded` needs the `WorkingSince` that the
    /// reduction is about to clear, and reducing first would leave nothing to
    /// measure the session against.
    public PetActivityResponse Observe(
        ActivityEvent evt,
        PetState state,
        System.DateTimeOffset now,
        ActivityContext? context = null)
    {
        context ??= ActivityContext.Quiet;
        var current = Advance(state, now, context);
        var needs = current.Needs;

        switch (evt.Kind)
        {
            case ActivityEventKind.DailyWake:
                return Celebrating(PetReaction.HappyToSeeYou,
                    happinessGain: 3, trustGain: 1,
                    xp: ProgressionRates.DailyWakeXP, source: XPSource.DailyWake,
                    state: current, now: now, day: evt.Timestamp);

            case ActivityEventKind.TaskCompleted:
                return Celebrating(PetReaction.CelebratedTask,
                    happinessGain: 4, trustGain: 0,
                    xp: ProgressionRates.TaskCompletedXP, source: XPSource.TaskCompleted,
                    state: current, now: now, day: evt.Timestamp);

            case ActivityEventKind.TaskFailed:
                // The only event that costs the pet anything. It shares the
                // setback rather than being punished for it — a small dip, and a
                // reaction that says so.
                return Response(current,
                    hunger: needs.Hunger + 4,
                    energy: needs.Energy - 3,
                    happiness: needs.Happiness - 3,
                    trust: needs.Trust,
                    now: now,
                    reaction: PetReaction.SharedSetback);

            case ActivityEventKind.Milestone:
                return Celebrating(PetReaction.ProudOfMilestone,
                    happinessGain: 6, trustGain: 2,
                    xp: ProgressionRates.MilestoneXP, source: XPSource.Milestone,
                    state: current, now: now, day: evt.Timestamp);

            case ActivityEventKind.UserReturned:
                return new PetActivityResponse(current, PetReaction.GladYouAreBack);

            case ActivityEventKind.WorkStarted:
                return new PetActivityResponse(current, PetReaction.StartedWorking);

            case ActivityEventKind.WorkEnded:
            {
                var updated = current;
                if (context.WorkingSince is System.DateTimeOffset workingSince)
                {
                    // Duration is measured between the EVENTS' own timestamps,
                    // never delivery time: a session drained late from the inbox
                    // must not be paid for the delay. A block that ended while
                    // the user was still away stops counting at the moment they
                    // left — a finished absence has already been discounted by
                    // the return path sliding WorkingSince forward.
                    var sessionEnd = context.IsUserPresent
                        ? evt.Timestamp
                        : Earlier(evt.Timestamp, context.AwaySince ?? evt.Timestamp);
                    double minutes =
                        System.Math.Max(0, (sessionEnd - workingSince).TotalSeconds) / 60;
                    if (minutes >= ProgressionRates.FocusSessionMinimumMinutes)
                    {
                        updated = GrantingXP(
                            minutes * ProgressionRates.FocusSessionXPPerMinute,
                            XPSource.FocusSession, updated, now,
                            day: evt.Timestamp, condition: needs);
                    }
                }
                return new PetActivityResponse(updated, PetReaction.TookABreak);
            }

            case ActivityEventKind.AwaitingInput:
                return new PetActivityResponse(current, PetReaction.WaitingOnYou);

            case ActivityEventKind.UserIdle:
                return new PetActivityResponse(current, PetReaction.NoticedYouAreAway);

            case ActivityEventKind.WorkLogged:
            {
                int count = current.WorkLog.Current(now, 0);
                var updated = UpdatedState(
                    current,
                    new PetNeeds(
                        hunger: needs.Hunger,
                        energy: needs.Energy,
                        happiness: needs.Happiness + Rates.WorkLogHappinessGain,
                        trust: needs.Trust),
                    now,
                    lastWorkLogAt: now,
                    workLog: new DailyTally<int>(count + 1, now));
                return new PetActivityResponse(
                    GrantingXP(ProgressionRates.WorkLoggedXP, XPSource.WorkLogged, updated,
                               now, day: evt.Timestamp, condition: needs),
                    PetReaction.LoggedWork);
            }

            // Swift's switch over the kind is exhaustive and has no default.
            // C# needs one; a kind added to the enum and not handled here should
            // still advance the pet, not throw at it.
            default:
                return new PetActivityResponse(current, null);
        }
    }

    private static System.DateTimeOffset Earlier(
        System.DateTimeOffset a, System.DateTimeOffset b) => a < b ? a : b;

    /// Whether logging work is allowed right now: a cooldown between logs and a
    /// hard daily cap, so a self-reported source — the least verifiable kind of
    /// event there is — cannot be farmed by clicking. There is deliberately no
    /// user-adjustable point value; every credited log is worth the same fixed
    /// amount, which is the actual fix for that failure mode.
    public PetActionAvailability WorkLogAvailability(
        PetState state, System.DateTimeOffset now)
    {
        if (state.LastWorkLogAt is System.DateTimeOffset last)
        {
            double elapsedMinutes = (now - last).TotalSeconds / 60;
            if (elapsedMinutes < Rates.WorkLogCooldownMinutes)
            {
                int remaining = System.Math.Max(1, (int)System.Math.Ceiling(
                    Rates.WorkLogCooldownMinutes - elapsedMinutes));
                return new PetActionAvailability(false,
                    remaining == 1
                        ? "Give it a minute before logging again."
                        : $"Give it {remaining} more minutes before logging again.");
            }
        }

        if (state.WorkLog.Current(now, 0) >= Rates.WorkLogDailyCap)
        {
            return new PetActionAvailability(false,
                $"{state.Name} has logged enough work for today.");
        }

        return new PetActionAvailability(true);
    }

    /// A share-worthy event's full effect: a happiness and trust bump, plus an XP
    /// grant whose condition multiplier reads the needs from *before* the bump,
    /// charged against the event's own day.
    private PetActivityResponse Celebrating(
        PetReaction reaction,
        double happinessGain,
        double trustGain,
        double xp,
        XPSource source,
        PetState state,
        System.DateTimeOffset now,
        System.DateTimeOffset day)
    {
        var needs = state.Needs;
        var updated = UpdatedState(
            state,
            new PetNeeds(
                hunger: needs.Hunger,
                energy: needs.Energy,
                happiness: needs.Happiness + happinessGain,
                trust: needs.Trust + trustGain),
            now);
        return new PetActivityResponse(
            GrantingXP(xp, source, updated, now, day: day, condition: needs),
            reaction);
    }

    /// A needs change with a reaction and no XP. The activity half's counterpart
    /// to `Result`, which is the care half's — the difference is that care always
    /// grants its trickle and an observed setback grants nothing.
    private PetActivityResponse Response(
        PetState state,
        double hunger, double energy, double happiness, double trust,
        System.DateTimeOffset now,
        PetReaction reaction) =>
        new(UpdatedState(state, new PetNeeds(hunger, energy, happiness, trust), now),
            reaction);

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
        System.DateTimeOffset? lastWorkLogAt = null,
        DailyTally<int>? workLog = null,
        double? totalXP = null,
        PetStats? stats = null,
        DailyTally<Dictionary<string, double>>? dailyXP = null,
        DailyTally<Dictionary<string, int>>? dailyEventCount = null) =>
        state.Advanced(
            needs: needs,
            at: now,
            lastWorkLogAt: lastWorkLogAt,
            workLog: workLog,
            totalXP: totalXP,
            stats: stats,
            dailyXP: dailyXP,
            dailyEventCount: dailyEventCount);
}
