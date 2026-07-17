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

public struct PetState: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 1

    public let schemaVersion: Int
    public let name: String
    public let needs: PetNeeds
    public let preferences: PetPreferences
    public let lastUpdatedAt: Date

    public init(
        schemaVersion: Int = PetState.currentSchemaVersion,
        name: String,
        needs: PetNeeds,
        preferences: PetPreferences,
        lastUpdatedAt: Date
    ) {
        self.schemaVersion = schemaVersion
        self.name = name
        self.needs = needs
        self.preferences = preferences
        self.lastUpdatedAt = lastUpdatedAt
    }

    public static func newPet(name: String = "Pixel", now: Date = Date()) -> PetState {
        PetState(
            name: name,
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
}

public struct PetInteractionResult: Equatable, Sendable {
    public let state: PetState
    public let reaction: PetReaction

    public init(state: PetState, reaction: PetReaction) {
        self.state = state
        self.reaction = reaction
    }
}
