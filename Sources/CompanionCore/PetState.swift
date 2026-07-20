import Foundation

public struct PetNeeds: Codable, Equatable, Sendable {
    public let hunger: Double
    public let energy: Double
    public let happiness: Double
    public let trust: Double

    public var fullness: Double {
        100 - hunger
    }

    public init(
        hunger: Double,
        energy: Double,
        happiness: Double,
        trust: Double
    ) {
        self.hunger = Self.clamp(hunger)
        self.energy = Self.clamp(energy)
        self.happiness = Self.clamp(happiness)
        self.trust = Self.clamp(trust)
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            hunger: try container.decode(Double.self, forKey: .hunger),
            energy: try container.decode(Double.self, forKey: .energy),
            happiness: try container.decode(Double.self, forKey: .happiness),
            trust: try container.decode(Double.self, forKey: .trust)
        )
    }

    private static func clamp(_ value: Double) -> Double {
        guard value.isFinite else {
            return 0
        }
        return min(max(value, 0), 100)
    }
}

public enum PetFood: String, CaseIterable, Codable, Equatable, Sendable {
    case berries
    case biscuit
    case noodles

    public var displayName: String {
        switch self {
        case .berries: "Berries"
        case .biscuit: "Biscuit"
        case .noodles: "Noodles"
        }
    }
}

public enum PetPlayActivity: String, CaseIterable, Codable, Equatable, Sendable {
    case chase
    case dance
    case puzzle

    public var displayName: String {
        switch self {
        case .chase: "Chase"
        case .dance: "Dance"
        case .puzzle: "Puzzle"
        }
    }
}

public struct PetPreferences: Codable, Equatable, Sendable {
    public let favouriteFood: PetFood
    public let favouritePlayActivity: PetPlayActivity

    public init(
        favouriteFood: PetFood,
        favouritePlayActivity: PetPlayActivity
    ) {
        self.favouriteFood = favouriteFood
        self.favouritePlayActivity = favouritePlayActivity
    }
}

public enum PetMood: String, Codable, Equatable, Sendable {
    case happy
    case content
    case hungry
    case sleepy
    case sad
    case wary
}

public enum PetFamily: String, CaseIterable, Codable, Equatable, Sendable {
    case wildkin
    case elemental
    case relicborn

    public var displayName: String {
        switch self {
        case .wildkin: "Wildkin"
        case .elemental: "Elemental"
        case .relicborn: "Relicborn"
        }
    }
}

public struct PetState: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 1

    public let schemaVersion: Int
    public let name: String
    public let family: PetFamily
    public let needs: PetNeeds
    public let preferences: PetPreferences
    public let lastUpdatedAt: Date
    /// Log Work fairness bookkeeping. `workLogCountToday` only reflects the
    /// calendar day named by `workLogCountDate`; a caller must compare dates
    /// before trusting the count, since it is never proactively reset.
    public let lastWorkLogAt: Date?
    public let workLogCountToday: Int
    public let workLogCountDate: Date?

    public init(
        schemaVersion: Int = PetState.currentSchemaVersion,
        name: String,
        family: PetFamily = .wildkin,
        needs: PetNeeds,
        preferences: PetPreferences,
        lastUpdatedAt: Date,
        lastWorkLogAt: Date? = nil,
        workLogCountToday: Int = 0,
        workLogCountDate: Date? = nil
    ) {
        self.schemaVersion = schemaVersion
        self.name = name
        self.family = family
        self.needs = needs
        self.preferences = preferences
        self.lastUpdatedAt = lastUpdatedAt
        self.lastWorkLogAt = lastWorkLogAt
        self.workLogCountToday = workLogCountToday
        self.workLogCountDate = workLogCountDate
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            schemaVersion: try container.decode(Int.self, forKey: .schemaVersion),
            name: try container.decode(String.self, forKey: .name),
            family: try container.decodeIfPresent(PetFamily.self, forKey: .family) ?? .wildkin,
            needs: try container.decode(PetNeeds.self, forKey: .needs),
            preferences: try container.decode(PetPreferences.self, forKey: .preferences),
            lastUpdatedAt: try container.decode(Date.self, forKey: .lastUpdatedAt),
            lastWorkLogAt: try container.decodeIfPresent(Date.self, forKey: .lastWorkLogAt),
            workLogCountToday: try container.decodeIfPresent(Int.self, forKey: .workLogCountToday) ?? 0,
            workLogCountDate: try container.decodeIfPresent(Date.self, forKey: .workLogCountDate)
        )
    }

    public static func newPet(
        name: String = "Pixel",
        family: PetFamily = .wildkin,
        now: Date = Date()
    ) -> PetState {
        PetState(
            name: name,
            family: family,
            needs: PetNeeds(
                hunger: 15,
                energy: 80,
                happiness: 70,
                trust: 50
            ),
            preferences: PetPreferences(
                favouriteFood: .berries,
                favouritePlayActivity: .puzzle
            ),
            lastUpdatedAt: now
        )
    }

    public func selectingFamily(_ family: PetFamily) -> PetState {
        PetState(
            schemaVersion: schemaVersion,
            name: name,
            family: family,
            needs: needs,
            preferences: preferences,
            lastUpdatedAt: lastUpdatedAt,
            lastWorkLogAt: lastWorkLogAt,
            workLogCountToday: workLogCountToday,
            workLogCountDate: workLogCountDate
        )
    }

    public var mood: PetMood {
        if needs.hunger >= 75 {
            return .hungry
        }
        if needs.energy <= 20 {
            return .sleepy
        }
        if needs.trust <= 20 {
            return .wary
        }
        if needs.happiness <= 30 {
            return .sad
        }
        if needs.happiness >= 75 && needs.trust >= 60 && needs.hunger <= 40 {
            return .happy
        }
        return .content
    }
}

public enum PetAction: Equatable, Sendable {
    case feed(PetFood)
    case play(PetPlayActivity)
    case pet
    case sleep
}

public enum PetReaction: String, Equatable, Sendable {
    case likedFood
    case lovedFood
    case enjoyedPlay
    case lovedPlay
    case comforted
    case rested
    case tooTiredToPlay
    case happyToSeeYou
    case celebratedTask
    case sharedSetback
    case proudOfMilestone
    case gladYouAreBack
    case startedWorking
    case tookABreak
    case waitingOnYou
    case noticedYouAreAway
    case loggedWork
}

public struct PetInteractionResult: Equatable, Sendable {
    public let state: PetState
    public let reaction: PetReaction

    public init(state: PetState, reaction: PetReaction) {
        self.state = state
        self.reaction = reaction
    }
}
