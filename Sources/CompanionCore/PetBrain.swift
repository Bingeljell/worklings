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

    public init(
        hungerPerHour: Double = 4,
        energyPerHour: Double = 3,
        happinessPerHour: Double = 1,
        maximumOfflineHours: Double = 24 * 7,
        workingHungerMultiplier: Double = 1.25,
        workingEnergyMultiplier: Double = 1.3,
        awayTrustPerHour: Double = 2,
        awayGracePeriodHours: Double = 1,
        longAwayTrustPerHour: Double = 0.2
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
    }

    /// Multiplies every per-hour rate by `factor`, so a real-time wait during
    /// manual testing can stand in for hours without touching event deltas
    /// or production tuning. The grace period is divided by the same factor
    /// so both tiers of the away-trust rate stay reachable within a short test.
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
            longAwayTrustPerHour: longAwayTrustPerHour * factor
        )
    }
}

public struct PetBrain: Sendable {
    public let rates: PetSimulationRates

    public init(rates: PetSimulationRates = PetSimulationRates()) {
        self.rates = rates
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
    /// and return a visible reaction. Effects are alpha tuning.
    public func observe(
        _ event: ActivityEvent,
        on state: PetState,
        at now: Date
    ) -> PetActivityResponse {
        let currentState = advance(state, to: now)
        let needs = currentState.needs

        switch event.kind {
        case .dailyWake:
            return response(
                from: currentState,
                happiness: needs.happiness + 3,
                trust: needs.trust + 1,
                at: now,
                reaction: .happyToSeeYou
            )

        case .taskCompleted:
            return response(
                from: currentState,
                happiness: needs.happiness + 4,
                trust: needs.trust,
                at: now,
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
            return response(
                from: currentState,
                happiness: needs.happiness + 6,
                trust: needs.trust + 2,
                at: now,
                reaction: .proudOfMilestone
            )

        case .userReturned:
            return PetActivityResponse(state: currentState, reaction: .gladYouAreBack)

        case .workStarted:
            return PetActivityResponse(state: currentState, reaction: .startedWorking)

        case .workEnded:
            return PetActivityResponse(state: currentState, reaction: .tookABreak)

        case .awaitingInput:
            return PetActivityResponse(state: currentState, reaction: .waitingOnYou)

        case .userIdle:
            return PetActivityResponse(state: currentState, reaction: .noticedYouAreAway)
        }
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

    private func result(
        from state: PetState,
        hunger: Double,
        energy: Double,
        happiness: Double,
        trust: Double,
        at now: Date,
        reaction: PetReaction
    ) -> PetInteractionResult {
        PetInteractionResult(
            state: updatedState(
                from: state,
                needs: PetNeeds(
                    hunger: hunger,
                    energy: energy,
                    happiness: happiness,
                    trust: trust
                ),
                at: now
            ),
            reaction: reaction
        )
    }

    private func updatedState(
        from state: PetState,
        needs: PetNeeds,
        at now: Date
    ) -> PetState {
        PetState(
            schemaVersion: state.schemaVersion,
            name: state.name,
            family: state.family,
            needs: needs,
            preferences: state.preferences,
            lastUpdatedAt: now
        )
    }
}
