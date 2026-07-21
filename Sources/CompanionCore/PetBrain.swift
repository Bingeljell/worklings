import Foundation

public struct PetSimulationRates: Equatable, Sendable {
    public let hungerPerHour: Double
    public let energyPerHour: Double
    public let happinessPerHour: Double
    public let maximumOfflineHours: Double
    public let workingHungerMultiplier: Double
    public let workingEnergyMultiplier: Double
    public let awayTrustPerHour: Double
    public let awayGracePeriodHours: Double
    public let longAwayTrustPerHour: Double
    public let workLogCooldownMinutes: Double
    public let workLogDailyCap: Int
    public let workLogHappinessGain: Double

    public init(
        hungerPerHour: Double = 4,
        energyPerHour: Double = 3,
        happinessPerHour: Double = 1,
        maximumOfflineHours: Double = 24 * 7,
        workingHungerMultiplier: Double = 1.25,
        workingEnergyMultiplier: Double = 1.3,
        awayTrustPerHour: Double = 2,
        awayGracePeriodHours: Double = 1,
        longAwayTrustPerHour: Double = 0.2,
        workLogCooldownMinutes: Double = 30,
        workLogDailyCap: Int = 6,
        workLogHappinessGain: Double = 3
    ) {
        self.hungerPerHour = max(hungerPerHour, 0)
        self.energyPerHour = max(energyPerHour, 0)
        self.happinessPerHour = max(happinessPerHour, 0)
        self.maximumOfflineHours = max(maximumOfflineHours, 0)
        self.workingHungerMultiplier = max(workingHungerMultiplier, 0)
        self.workingEnergyMultiplier = max(workingEnergyMultiplier, 0)
        self.awayTrustPerHour = max(awayTrustPerHour, 0)
        self.awayGracePeriodHours = max(awayGracePeriodHours, 0)
        self.longAwayTrustPerHour = max(longAwayTrustPerHour, 0)
        self.workLogCooldownMinutes = max(workLogCooldownMinutes, 0)
        self.workLogDailyCap = max(workLogDailyCap, 0)
        self.workLogHappinessGain = max(workLogHappinessGain, 0)
    }

    /// Multiplies every per-hour rate by `factor`, so a real-time wait during
    /// manual testing can stand in for hours without touching event deltas
    /// or production tuning. The grace period and the Log Work cooldown are
    /// divided by the same factor so every gated tier stays reachable within
    /// a short test; the daily cap and per-log gain are flat amounts, not
    /// rates, so they are left untouched.
    public func scaled(by factor: Double) -> PetSimulationRates {
        PetSimulationRates(
            hungerPerHour: hungerPerHour * factor,
            energyPerHour: energyPerHour * factor,
            happinessPerHour: happinessPerHour * factor,
            maximumOfflineHours: maximumOfflineHours,
            workingHungerMultiplier: workingHungerMultiplier,
            workingEnergyMultiplier: workingEnergyMultiplier,
            awayTrustPerHour: awayTrustPerHour * factor,
            awayGracePeriodHours: factor > 0 ? awayGracePeriodHours / factor : awayGracePeriodHours,
            longAwayTrustPerHour: longAwayTrustPerHour * factor,
            workLogCooldownMinutes: factor > 0 ? workLogCooldownMinutes / factor : workLogCooldownMinutes,
            workLogDailyCap: workLogDailyCap,
            workLogHappinessGain: workLogHappinessGain
        )
    }
}

public struct PetBrain: Sendable {
    public let rates: PetSimulationRates
    public let progressionRates: PetProgressionRates

    public init(
        rates: PetSimulationRates = PetSimulationRates(),
        progressionRates: PetProgressionRates = PetProgressionRates()
    ) {
        self.rates = rates
        self.progressionRates = progressionRates
    }

    public func advance(
        _ state: PetState,
        to now: Date,
        context: ActivityContext = .quiet
    ) -> PetState {
        let elapsedSeconds = now.timeIntervalSince(state.lastUpdatedAt)
        guard elapsedSeconds > 0 else {
            return state
        }

        let elapsedHours = min(elapsedSeconds / 3_600, rates.maximumOfflineHours)
        let hungerMultiplier = context.isWorking ? rates.workingHungerMultiplier : 1
        let energyMultiplier = context.isWorking ? rates.workingEnergyMultiplier : 1
        let hunger = state.needs.hunger + rates.hungerPerHour * hungerMultiplier * elapsedHours
        let energy = state.needs.energy - rates.energyPerHour * energyMultiplier * elapsedHours

        let hungerPenalty = max(hunger - 75, 0) / 25
        let exhaustionPenalty = max(20 - energy, 0) / 20
        let distress = hungerPenalty + exhaustionPenalty

        let happiness = state.needs.happiness
            - rates.happinessPerHour * elapsedHours
            - distress * 0.75 * elapsedHours
        let awayTrustDrain = awayTrustRate(for: context, at: now) * elapsedHours
        let trust = state.needs.trust - distress * 0.2 * elapsedHours - awayTrustDrain

        return updatedState(
            from: state,
            needs: PetNeeds(
                hunger: hunger,
                energy: energy,
                happiness: happiness,
                trust: trust
            ),
            at: now
        )
    }

    public func perform(
        _ action: PetAction,
        on state: PetState,
        at now: Date
    ) -> PetInteractionResult {
        let currentState = advance(state, to: now)
        let needs = currentState.needs

        switch action {
        case let .feed(food):
            let isFavourite = food == currentState.preferences.favouriteFood
            return result(
                from: currentState,
                hunger: needs.hunger - (isFavourite ? 30 : 20),
                energy: needs.energy,
                happiness: needs.happiness + (isFavourite ? 8 : 3),
                trust: needs.trust + (isFavourite ? 3 : 1),
                at: now,
                reaction: isFavourite ? .lovedFood : .likedFood
            )

        case let .play(activity):
            guard needs.energy >= 15 else {
                return PetInteractionResult(
                    state: currentState,
                    reaction: .tooTiredToPlay
                )
            }

            let isFavourite = activity == currentState.preferences.favouritePlayActivity
            return result(
                from: currentState,
                hunger: needs.hunger + (isFavourite ? 8 : 7),
                energy: needs.energy - (isFavourite ? 14 : 12),
                happiness: needs.happiness + (isFavourite ? 22 : 14),
                trust: needs.trust + (isFavourite ? 6 : 3),
                at: now,
                reaction: isFavourite ? .lovedPlay : .enjoyedPlay
            )

        case .pet:
            return result(
                from: currentState,
                hunger: needs.hunger,
                energy: needs.energy,
                happiness: needs.happiness + 8,
                trust: needs.trust + 4,
                at: now,
                reaction: .comforted
            )

        case .sleep:
            return result(
                from: currentState,
                hunger: needs.hunger + 6,
                energy: needs.energy + 35,
                happiness: needs.happiness + 2,
                trust: needs.trust,
                at: now,
                reaction: .rested
            )
        }
    }

    /// Applies an observed activity event to the pet. Structural events shape
    /// the activity context only; moments worth sharing move needs slightly
    /// and return a visible reaction. `context` is the activity context from
    /// *before* this event was reduced into it, needed only to recover
    /// `workingSince` for a `workEnded` session-duration check. Effects are
    /// alpha tuning.
    public func observe(
        _ event: ActivityEvent,
        on state: PetState,
        at now: Date,
        context: ActivityContext = .quiet
    ) -> PetActivityResponse {
        let currentState = advance(state, to: now)
        let needs = currentState.needs

        switch event.kind {
        case .dailyWake:
            let updated = updatedState(
                from: currentState,
                needs: PetNeeds(
                    hunger: needs.hunger,
                    energy: needs.energy,
                    happiness: needs.happiness + 3,
                    trust: needs.trust + 1
                ),
                at: now
            )
            return PetActivityResponse(
                state: grantingXP(progressionRates.dailyWakeXP, source: .dailyWake, to: updated, at: now),
                reaction: .happyToSeeYou
            )

        case .taskCompleted:
            let updated = updatedState(
                from: currentState,
                needs: PetNeeds(
                    hunger: needs.hunger,
                    energy: needs.energy,
                    happiness: needs.happiness + 4,
                    trust: needs.trust
                ),
                at: now
            )
            return PetActivityResponse(
                state: grantingXP(
                    progressionRates.taskCompletedXP,
                    source: .taskCompleted,
                    to: updated,
                    at: now
                ),
                reaction: .celebratedTask
            )

        case .taskFailed:
            return response(
                from: currentState,
                hunger: needs.hunger + 4,
                energy: needs.energy - 3,
                happiness: needs.happiness - 3,
                trust: needs.trust,
                at: now,
                reaction: .sharedSetback
            )

        case .milestone:
            let updated = updatedState(
                from: currentState,
                needs: PetNeeds(
                    hunger: needs.hunger,
                    energy: needs.energy,
                    happiness: needs.happiness + 6,
                    trust: needs.trust + 2
                ),
                at: now
            )
            return PetActivityResponse(
                state: grantingXP(progressionRates.milestoneXP, source: .milestone, to: updated, at: now),
                reaction: .proudOfMilestone
            )

        case .userReturned:
            return PetActivityResponse(state: currentState, reaction: .gladYouAreBack)

        case .workStarted:
            return PetActivityResponse(state: currentState, reaction: .startedWorking)

        case .workEnded:
            var updated = currentState
            if let workingSince = context.workingSince {
                let minutes = now.timeIntervalSince(workingSince) / 60
                if minutes >= progressionRates.focusSessionMinimumMinutes {
                    updated = grantingXP(
                        minutes * progressionRates.focusSessionXPPerMinute,
                        source: .focusSession,
                        to: updated,
                        at: now
                    )
                }
            }
            return PetActivityResponse(state: updated, reaction: .tookABreak)

        case .awaitingInput:
            return PetActivityResponse(state: currentState, reaction: .waitingOnYou)

        case .userIdle:
            return PetActivityResponse(state: currentState, reaction: .noticedYouAreAway)

        case .workLogged:
            let count = currentWorkLogCount(state: currentState, at: now)
            let updated = updatedState(
                from: currentState,
                needs: PetNeeds(
                    hunger: needs.hunger,
                    energy: needs.energy,
                    happiness: needs.happiness + rates.workLogHappinessGain,
                    trust: needs.trust
                ),
                at: now,
                lastWorkLogAt: now,
                workLogCountToday: count + 1,
                workLogCountDate: now
            )
            return PetActivityResponse(
                state: grantingXP(progressionRates.workLoggedXP, source: .workLogged, to: updated, at: now),
                reaction: .loggedWork
            )
        }
    }

    /// Whether logging work is currently allowed: a cooldown between logs and
    /// a hard daily cap, so a self-reported source — the least verifiable
    /// kind of event — cannot be farmed by repeated clicking. There is no
    /// user-adjustable point value; every credited log grants the same fixed
    /// amount, which is the actual fix for that failure mode.
    public func workLogAvailability(state: PetState, at now: Date) -> PetActionAvailability {
        if let lastWorkLogAt = state.lastWorkLogAt {
            let elapsedMinutes = now.timeIntervalSince(lastWorkLogAt) / 60
            if elapsedMinutes < rates.workLogCooldownMinutes {
                let remaining = max(1, Int((rates.workLogCooldownMinutes - elapsedMinutes).rounded(.up)))
                return PetActionAvailability(
                    isEnabled: false,
                    explanation: remaining == 1
                        ? "Give it a minute before logging again."
                        : "Give it \(remaining) more minutes before logging again."
                )
            }
        }

        guard currentWorkLogCount(state: state, at: now) < rates.workLogDailyCap else {
            return PetActionAvailability(
                isEnabled: false,
                explanation: "\(state.name) has logged enough work for today."
            )
        }

        return PetActionAvailability(isEnabled: true)
    }

    /// `workLogCountToday` is only meaningful for the calendar day named by
    /// `workLogCountDate`; a stale count from a previous day is never reset
    /// in storage, only ignored here, so the save never needs a day-rollover
    /// side effect.
    private func currentWorkLogCount(
        state: PetState,
        at now: Date,
        calendar: Calendar = .current
    ) -> Int {
        guard let workLogCountDate = state.workLogCountDate,
              calendar.isDate(workLogCountDate, inSameDayAs: now) else {
            return 0
        }
        return state.workLogCountToday
    }

    private func response(
        from state: PetState,
        hunger: Double? = nil,
        energy: Double? = nil,
        happiness: Double,
        trust: Double,
        at now: Date,
        reaction: PetReaction
    ) -> PetActivityResponse {
        PetActivityResponse(
            state: updatedState(
                from: state,
                needs: PetNeeds(
                    hunger: hunger ?? state.needs.hunger,
                    energy: energy ?? state.needs.energy,
                    happiness: happiness,
                    trust: trust
                ),
                at: now
            ),
            reaction: reaction
        )
    }

    /// The two-tier away rate: a full-strength rate for a short absence,
    /// tapering to a gentle trickle beyond the grace period so an evening or
    /// a weekend away costs far less than the same duration would at the
    /// short-absence rate. Applies the single rate reached by `now` across
    /// the whole tick, an approximation that matters only for one unusually
    /// large gap (e.g. the Mac slept through the tier boundary); at the
    /// normal one-tick-per-minute cadence the tiers land correctly.
    private func awayTrustRate(for context: ActivityContext, at now: Date) -> Double {
        guard !context.isUserPresent else {
            return 0
        }
        let awayHours = max(0, now.timeIntervalSince(context.awaySince ?? now)) / 3_600
        return awayHours > rates.awayGracePeriodHours
            ? rates.longAwayTrustPerHour
            : rates.awayTrustPerHour
    }

    /// Care actions grant a small trickle of XP on every successful outcome
    /// (refusals like "too tired to play" never reach this helper), so
    /// tending the pet always means something toward the character sheet,
    /// not just its condition.
    private func result(
        from state: PetState,
        hunger: Double,
        energy: Double,
        happiness: Double,
        trust: Double,
        at now: Date,
        reaction: PetReaction
    ) -> PetInteractionResult {
        let updated = updatedState(
            from: state,
            needs: PetNeeds(
                hunger: hunger,
                energy: energy,
                happiness: happiness,
                trust: trust
            ),
            at: now
        )
        return PetInteractionResult(
            state: grantingXP(progressionRates.careActionXP, source: .care, to: updated, at: now),
            reaction: reaction
        )
    }

    /// Grants XP from `source`, subject to the condition multiplier, a
    /// per-source daily cap, and an overall daily cap — the actual fairness
    /// mechanism (see the progression design's "caps, not cryptography").
    /// Crossing a level threshold applies that many levels' worth of
    /// class-weighted stat growth in the same step, so one large grant can
    /// never skip growth for an intermediate level.
    private func grantingXP(
        _ rawAmount: Double,
        source: XPSource,
        to state: PetState,
        at now: Date,
        calendar: Calendar = .current
    ) -> PetState {
        guard rawAmount > 0 else {
            return state
        }

        let isSameDay = state.dailyXPDate.map { calendar.isDate($0, inSameDayAs: now) } ?? false
        let dailyXPBySource = isSameDay ? state.dailyXPBySource : [:]

        let grantedTodayForSource = dailyXPBySource[source.rawValue] ?? 0
        let grantedTodayOverall = dailyXPBySource.values.reduce(0, +)

        let sourceHeadroom = max(0, progressionRates.dailyCap(for: source) - grantedTodayForSource)
        let overallHeadroom = max(0, progressionRates.overallDailyCap - grantedTodayOverall)

        let multiplier = state.needs.xpMultiplier(floor: progressionRates.conditionMultiplierFloor)
        let amount = min(rawAmount * multiplier, sourceHeadroom, overallHeadroom)
        guard amount > 0 else {
            return state
        }

        var updatedDailyXPBySource = dailyXPBySource
        updatedDailyXPBySource[source.rawValue] = grantedTodayForSource + amount

        let newTotalXP = state.totalXP + amount
        let levelsGained = PetProgressionCurve.level(forTotalXP: newTotalXP)
            - PetProgressionCurve.level(forTotalXP: state.totalXP)

        var stats = state.stats
        if levelsGained > 0 {
            let signatureStat = state.petClass.signatureStat
            for _ in 0..<levelsGained {
                stats = stats.growing(
                    signatureStat: signatureStat,
                    signatureGain: progressionRates.signatureStatGainPerLevel,
                    otherGain: progressionRates.otherStatGainPerLevel
                )
            }
        }

        return updatedState(
            from: state,
            needs: state.needs,
            at: now,
            totalXP: newTotalXP,
            stats: stats,
            dailyXPBySource: updatedDailyXPBySource,
            dailyXPDate: now
        )
    }

    private func updatedState(
        from state: PetState,
        needs: PetNeeds,
        at now: Date,
        lastWorkLogAt: Date? = nil,
        workLogCountToday: Int? = nil,
        workLogCountDate: Date? = nil,
        totalXP: Double? = nil,
        stats: PetStats? = nil,
        dailyXPBySource: [String: Double]? = nil,
        dailyXPDate: Date? = nil
    ) -> PetState {
        PetState(
            schemaVersion: state.schemaVersion,
            name: state.name,
            family: state.family,
            needs: needs,
            preferences: state.preferences,
            lastUpdatedAt: now,
            lastWorkLogAt: lastWorkLogAt ?? state.lastWorkLogAt,
            workLogCountToday: workLogCountToday ?? state.workLogCountToday,
            workLogCountDate: workLogCountDate ?? state.workLogCountDate,
            totalXP: totalXP ?? state.totalXP,
            petClass: state.petClass,
            stats: stats ?? state.stats,
            dailyXPBySource: dailyXPBySource ?? state.dailyXPBySource,
            dailyXPDate: dailyXPDate ?? state.dailyXPDate
        )
    }
}
