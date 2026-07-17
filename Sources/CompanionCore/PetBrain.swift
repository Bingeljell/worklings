import Foundation

public struct PetSimulationRates: Equatable, Sendable {
    public let hungerPerHour: Double
    public let energyPerHour: Double
    public let happinessPerHour: Double
    public let maximumOfflineHours: Double

    public init(
        hungerPerHour: Double = 4,
        energyPerHour: Double = 3,
        happinessPerHour: Double = 1,
        maximumOfflineHours: Double = 24 * 7
    ) {
        self.hungerPerHour = max(hungerPerHour, 0)
        self.energyPerHour = max(energyPerHour, 0)
        self.happinessPerHour = max(happinessPerHour, 0)
        self.maximumOfflineHours = max(maximumOfflineHours, 0)
    }
}

public struct PetBrain: Sendable {
    public let rates: PetSimulationRates

    public init(rates: PetSimulationRates = PetSimulationRates()) {
        self.rates = rates
    }

    public func advance(_ state: PetState, to now: Date) -> PetState {
        let elapsedSeconds = now.timeIntervalSince(state.lastUpdatedAt)
        guard elapsedSeconds > 0 else {
            return state
        }

        let elapsedHours = min(elapsedSeconds / 3_600, rates.maximumOfflineHours)
        let hunger = state.needs.hunger + rates.hungerPerHour * elapsedHours
        let energy = state.needs.energy - rates.energyPerHour * elapsedHours

        let hungerPenalty = max(hunger - 75, 0) / 25
        let exhaustionPenalty = max(20 - energy, 0) / 20
        let distress = hungerPenalty + exhaustionPenalty

        let happiness = state.needs.happiness
            - rates.happinessPerHour * elapsedHours
            - distress * 0.75 * elapsedHours
        let trust = state.needs.trust - distress * 0.2 * elapsedHours

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
