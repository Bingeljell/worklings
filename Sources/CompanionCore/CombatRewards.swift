/// How a delve ended, from the HP the pet walked out with. Sets both the
/// narration and the condition aftermath.
public enum ExitTier: Equatable, Sendable {
    case flawless  // ≥ 90% HP
    case solid     // 40–90%
    case barely    // < 40%
    case downed    // retreated at 0

    /// Flawless/Solid/Barely on a win, from the HP fraction; Downed on a loss.
    public static func forOutcome(victory: Bool, hpFraction: Double) -> ExitTier {
        guard victory else { return .downed }
        if hpFraction >= 0.9 { return .flawless }
        if hpFraction >= 0.4 { return .solid }
        return .barely
    }
}

/// A signed change to each of the four conditions. Expressed in **Fullness**
/// terms (higher is better) to match the rest of the design; the write-back
/// converts Fullness back to the stored hunger.
public struct ConditionDelta: Equatable, Sendable {
    public let fullness: Double
    public let energy: Double
    public let happiness: Double
    public let trust: Double

    public init(fullness: Double, energy: Double, happiness: Double, trust: Double) {
        self.fullness = fullness
        self.energy = energy
        self.happiness = happiness
        self.trust = trust
    }
}

extension PetCombatRates {
    /// The exit-tier condition deltas from `docs/design/dungeons.md`: a triumph
    /// lifts all four, an ordeal wears them all down. Held knobs — living as
    /// literals here rather than sixteen init parameters until they're tuned.
    /// Every magnitude stays inside the reversible-neglect envelope; the needs
    /// clamp on write-back is the final backstop.
    public func exitConditionDelta(for tier: ExitTier) -> ConditionDelta {
        switch tier {
        case .flawless:
            ConditionDelta(fullness: 2, energy: 2, happiness: 10, trust: 5)
        case .solid:
            ConditionDelta(fullness: -5, energy: -8, happiness: 5, trust: 2)
        case .barely:
            ConditionDelta(fullness: -10, energy: -15, happiness: -5, trust: 0)
        case .downed:
            ConditionDelta(fullness: -12, energy: -20, happiness: -12, trust: -6)
        }
    }
}

/// The result of applying a finished encounter to a pet: the updated state, the
/// tier reached, and the XP granted.
public struct EncounterResolution: Equatable, Sendable {
    public let state: PetState
    public let tier: ExitTier
    public let xpGained: Double

    public init(state: PetState, tier: ExitTier, xpGained: Double) {
        self.state = state
        self.tier = tier
        self.xpGained = xpGained
    }
}

extension PetState {
    /// Applies a **finished** encounter's result: grants the foe's reward XP on a
    /// win (none on a defeat) and moves all four conditions by the exit tier.
    /// Needs clamp themselves, so a disastrous fight drains the pet without ever
    /// breaking it — the reversible-neglect envelope holds.
    ///
    /// Dungeon XP is added directly here for the vertical slice; a separate
    /// dungeon daily-cap channel is a later refinement (see the design's open
    /// questions).
    public func applyingOutcome(
        of encounter: CombatEncounter,
        foe: Foe,
        rates: PetCombatRates
    ) -> EncounterResolution {
        let victory = encounter.status == .petVictory
        let tier = ExitTier.forOutcome(
            victory: victory, hpFraction: encounter.pet.hpFraction
        )
        let delta = rates.exitConditionDelta(for: tier)
        // Fullness rises as hunger falls, so a Fullness gain is a hunger cut.
        let updatedNeeds = PetNeeds(
            hunger: needs.hunger - delta.fullness,
            energy: needs.energy + delta.energy,
            happiness: needs.happiness + delta.happiness,
            trust: needs.trust + delta.trust
        )
        let xp = victory ? foe.rewardXP : 0
        return EncounterResolution(
            state: applying(needs: updatedNeeds, addingXP: xp),
            tier: tier,
            xpGained: xp
        )
    }
}
