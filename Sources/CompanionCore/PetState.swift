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
    public static let currentSchemaVersion = 2

    public let schemaVersion: Int
    public let name: String
    public let family: PetFamily
    public let needs: PetNeeds
    public let preferences: PetPreferences
    public let lastUpdatedAt: Date
    public let lastWorkLogAt: Date?
    /// Log Work fairness bookkeeping: how many logs were credited on its
    /// stored day. Read through `DailyTally.current(on:)` so a stale count is
    /// ignored rather than proactively reset.
    public let workLog: DailyTally<Int>
    public let totalXP: Double
    public let petClass: PetClass
    public let stats: PetStats
    /// XP granted on its stored day, keyed by `XPSource.rawValue`. Same
    /// day-scoped semantics as `workLog`.
    public let dailyXP: DailyTally<[String: Double]>

    public init(
        schemaVersion: Int = PetState.currentSchemaVersion,
        name: String,
        family: PetFamily = .wildkin,
        needs: PetNeeds,
        preferences: PetPreferences,
        lastUpdatedAt: Date,
        lastWorkLogAt: Date? = nil,
        workLog: DailyTally<Int> = DailyTally(value: 0),
        totalXP: Double = 0,
        petClass: PetClass = .wellspring,
        stats: PetStats = PetStats(),
        dailyXP: DailyTally<[String: Double]> = DailyTally(value: [:])
    ) {
        self.schemaVersion = schemaVersion
        self.name = name
        self.family = family
        self.needs = needs
        self.preferences = preferences
        self.lastUpdatedAt = lastUpdatedAt
        self.lastWorkLogAt = lastWorkLogAt
        self.workLog = workLog
        self.totalXP = max(totalXP, 0)
        self.petClass = petClass
        self.stats = stats
        self.dailyXP = dailyXP
    }

    private enum CodingKeys: String, CodingKey {
        case schemaVersion, name, family, needs, preferences, lastUpdatedAt
        case lastWorkLogAt, workLog, totalXP, petClass, stats, dailyXP
    }

    /// The pre-v2 flat daily fields, read only to fold a v1 save into the
    /// unified tallies. Kept separate from `CodingKeys` so the synthesized
    /// encoder never writes them back.
    private enum LegacyCodingKeys: String, CodingKey {
        case workLogCountToday, workLogCountDate, dailyXPBySource, dailyXPDate
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let legacy = try decoder.container(keyedBy: LegacyCodingKeys.self)

        let workLog = try container.decodeIfPresent(DailyTally<Int>.self, forKey: .workLog)
            ?? DailyTally(
                date: try legacy.decodeIfPresent(Date.self, forKey: .workLogCountDate),
                value: try legacy.decodeIfPresent(Int.self, forKey: .workLogCountToday) ?? 0
            )
        let dailyXP = try container.decodeIfPresent(
            DailyTally<[String: Double]>.self,
            forKey: .dailyXP
        )
            ?? DailyTally(
                date: try legacy.decodeIfPresent(Date.self, forKey: .dailyXPDate),
                value: try legacy.decodeIfPresent([String: Double].self, forKey: .dailyXPBySource) ?? [:]
            )

        self.init(
            schemaVersion: try container.decode(Int.self, forKey: .schemaVersion),
            name: try container.decode(String.self, forKey: .name),
            family: try container.decodeIfPresent(PetFamily.self, forKey: .family) ?? .wildkin,
            needs: try container.decode(PetNeeds.self, forKey: .needs),
            preferences: try container.decode(PetPreferences.self, forKey: .preferences),
            lastUpdatedAt: try container.decode(Date.self, forKey: .lastUpdatedAt),
            lastWorkLogAt: try container.decodeIfPresent(Date.self, forKey: .lastWorkLogAt),
            workLog: workLog,
            totalXP: try container.decodeIfPresent(Double.self, forKey: .totalXP) ?? 0,
            petClass: try container.decodeIfPresent(PetClass.self, forKey: .petClass) ?? .wellspring,
            stats: try container.decodeIfPresent(PetStats.self, forKey: .stats) ?? PetStats(),
            dailyXP: dailyXP
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

    /// The single full-field copy for the identity withers below, so adding
    /// a stored field means updating exactly one copy site instead of one
    /// per wither — a missed field here fails to compile rather than
    /// silently dropping state.
    private func replacing(
        schemaVersion: Int? = nil,
        name: String? = nil,
        family: PetFamily? = nil,
        petClass: PetClass? = nil
    ) -> PetState {
        PetState(
            schemaVersion: schemaVersion ?? self.schemaVersion,
            name: name ?? self.name,
            family: family ?? self.family,
            needs: needs,
            preferences: preferences,
            lastUpdatedAt: lastUpdatedAt,
            lastWorkLogAt: lastWorkLogAt,
            workLog: workLog,
            totalXP: totalXP,
            petClass: petClass ?? self.petClass,
            stats: stats,
            dailyXP: dailyXP
        )
    }

    /// Restamps the schema version, preserving every field. Used by the file
    /// store to finish migrating a loaded older save to the current version;
    /// the field-level upgrade already happened during decode.
    func upgradedToSchema(_ version: Int) -> PetState {
        replacing(schemaVersion: version)
    }

    public func selectingFamily(_ family: PetFamily) -> PetState {
        replacing(family: family)
    }

    /// Class is freely reassignable, the same way family is — there is
    /// nothing yet (no ability trees, no gear) that a class swap would need
    /// to protect. Stat growth already earned never changes; only future
    /// growth follows the new class's signature stat.
    public func selectingClass(_ petClass: PetClass) -> PetState {
        replacing(petClass: petClass)
    }

    public static let maximumNameLength = 24

    public static func isValidName(_ name: String) -> Bool {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        return !trimmed.isEmpty && trimmed.count <= maximumNameLength
    }

    /// Returns the pet unchanged if `name` isn't valid once trimmed, so a
    /// caller can attempt a rename without first duplicating the validation.
    public func renamed(to name: String) -> PetState {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard Self.isValidName(trimmed) else {
            return self
        }

        return replacing(name: trimmed)
    }

    /// Derived from `totalXP` rather than stored, so level and XP can never
    /// disagree with each other. See `PetProgressionCurve`.
    public var level: Int {
        PetProgressionCurve.level(forTotalXP: totalXP)
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
