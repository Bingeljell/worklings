import Foundation

/// Which of the five stats a class's growth favors. Kept distinct from the
/// stats themselves so a class can point at one without a per-class switch
/// inside `PetStats`.
public enum PetStatKind: String, CaseIterable, Codable, Equatable, Sendable {
    case vitality
    case power
    case defense
    case agility
    case wit

    /// The internal name is `defense`, not `guard` (a reserved word); the
    /// design vocabulary calls this stat "Guard" everywhere else, the same
    /// split already used for hunger/Fullness.
    public var displayName: String {
        switch self {
        case .vitality: "Vitality"
        case .power: "Power"
        case .defense: "Guard"
        case .agility: "Agility"
        case .wit: "Wit"
        }
    }
}

/// The battle-facing character sheet. Only ever grows — see
/// `PetProgressionCurve` for how level-ups apply that growth. Gear and class
/// modifiers, when they exist, compute an *effective* value on top of these
/// base numbers rather than ever being persisted into them.
public struct PetStats: Codable, Equatable, Sendable {
    public static let startingValue = 5

    public let vitality: Int
    public let power: Int
    public let defense: Int
    public let agility: Int
    public let wit: Int

    public init(
        vitality: Int = PetStats.startingValue,
        power: Int = PetStats.startingValue,
        defense: Int = PetStats.startingValue,
        agility: Int = PetStats.startingValue,
        wit: Int = PetStats.startingValue
    ) {
        self.vitality = vitality
        self.power = power
        self.defense = defense
        self.agility = agility
        self.wit = wit
    }

    public func value(for stat: PetStatKind) -> Int {
        switch stat {
        case .vitality: vitality
        case .power: power
        case .defense: defense
        case .agility: agility
        case .wit: wit
        }
    }

    /// Applies one level's worth of growth: `signatureStat` grows by
    /// `signatureGain`, every other stat grows by `otherGain`, so a class's
    /// identity is visible on the sheet from level one without any stat
    /// ever staying frozen.
    public func growing(
        signatureStat: PetStatKind,
        signatureGain: Int,
        otherGain: Int
    ) -> PetStats {
        func gain(for stat: PetStatKind) -> Int {
            stat == signatureStat ? signatureGain : otherGain
        }
        return PetStats(
            vitality: vitality + gain(for: .vitality),
            power: power + gain(for: .power),
            defense: defense + gain(for: .defense),
            agility: agility + gain(for: .agility),
            wit: wit + gain(for: .wit)
        )
    }
}

/// The mechanical-identity axis, separate from `PetFamily` (which stays
/// cosmetic). Each class has one signature stat that grows fastest on
/// level-up; every name is deliberately dual-coded, a term with real
/// currency in modern work/maker culture that also carries its own mythic
/// weight, independent of any RPG convention.
public enum PetClass: String, CaseIterable, Codable, Equatable, Sendable {
    case wellspring
    case juggernaut
    case aegis
    case maverick
    case tinkerer

    public var displayName: String {
        switch self {
        case .wellspring: "Wellspring"
        case .juggernaut: "Juggernaut"
        case .aegis: "Aegis"
        case .maverick: "Maverick"
        case .tinkerer: "Tinkerer"
        }
    }

    public var role: String {
        switch self {
        case .wellspring: "Healer"
        case .juggernaut: "Heavy Offense"
        case .aegis: "Tank"
        case .maverick: "Finesse Offense"
        case .tinkerer: "Mage-equivalent"
        }
    }

    public var signatureStat: PetStatKind {
        switch self {
        case .wellspring: .vitality
        case .juggernaut: .power
        case .aegis: .defense
        case .maverick: .agility
        case .tinkerer: .wit
        }
    }
}

/// Derives level from cumulative XP via a formula rather than a stored
/// value, so level and XP can never disagree with each other — the same
/// silent-desync failure mode this codebase has already hit twice with
/// other duplicated state (see the changelog for the Log Work fix).
public enum PetProgressionCurve {
    /// Cumulative XP required to have reached `level`. Quadratic growth
    /// keeps early levels cheap and later ones meaningfully longer; the
    /// formula has no upper bound, so raising a level cap later never
    /// requires migrating this table.
    public static func totalXPRequired(forLevel level: Int) -> Double {
        guard level > 1 else {
            return 0
        }
        let steps = Double(level - 1)
        return 50 * steps * (steps + 1)
    }

    public static func level(forTotalXP totalXP: Double) -> Int {
        var level = 1
        while totalXP >= totalXPRequired(forLevel: level + 1) {
            level += 1
        }
        return level
    }

    /// Everything a progress readout needs, derived once: the level, how far
    /// into it the total is, the level's full span, and the clamped 0...1
    /// fraction. Any surface showing an XP bar reads this instead of
    /// re-deriving the arithmetic.
    public struct Progress: Equatable, Sendable {
        public let level: Int
        public let xpIntoLevel: Double
        public let xpForLevel: Double
        public let fraction: Double
    }

    public static func progress(forTotalXP totalXP: Double) -> Progress {
        let level = level(forTotalXP: totalXP)
        let currentLevelXP = totalXPRequired(forLevel: level)
        let nextLevelXP = totalXPRequired(forLevel: level + 1)
        let xpIntoLevel = max(0, totalXP - currentLevelXP)
        let xpForLevel = nextLevelXP - currentLevelXP
        let fraction = xpForLevel > 0 ? min(max(xpIntoLevel / xpForLevel, 0), 1) : 1
        return Progress(
            level: level,
            xpIntoLevel: xpIntoLevel,
            xpForLevel: xpForLevel,
            fraction: fraction
        )
    }
}

/// Identifies which XP source a grant came from, purely for per-source
/// daily-cap bookkeeping. Distinct from `ActivityEvent.sourceId`, which
/// identifies *who reported* an event (system/manual/simulated); this
/// identifies *what kind of progress* it represents.
public enum XPSource: String, CaseIterable, Equatable, Sendable {
    case dailyWake
    case focusSession
    case care
    case taskCompleted
    case milestone
    case workLogged
}

/// Every number here is alpha tuning, the same posture as
/// `PetSimulationRates`: sane defaults now, retuned from real usage later
/// without touching the mechanism.
public struct PetProgressionRates: Equatable, Sendable {
    public let dailyWakeXP: Double
    public let focusSessionXPPerMinute: Double
    public let focusSessionMinimumMinutes: Double
    public let focusSessionDailyCap: Double
    public let careActionXP: Double
    public let careActionDailyCap: Double
    public let taskCompletedXP: Double
    public let taskCompletedDailyCap: Double
    public let milestoneXP: Double
    public let milestoneDailyCap: Double
    public let workLoggedXP: Double
    public let workLoggedDailyCap: Double
    public let overallDailyCap: Double
    public let signatureStatGainPerLevel: Int
    public let otherStatGainPerLevel: Int
    public let conditionMultiplierFloor: Double

    public init(
        dailyWakeXP: Double = 20,
        focusSessionXPPerMinute: Double = 2,
        focusSessionMinimumMinutes: Double = 10,
        focusSessionDailyCap: Double = 200,
        careActionXP: Double = 3,
        careActionDailyCap: Double = 60,
        taskCompletedXP: Double = 15,
        taskCompletedDailyCap: Double = 150,
        milestoneXP: Double = 40,
        milestoneDailyCap: Double = 200,
        workLoggedXP: Double = 5,
        workLoggedDailyCap: Double = 30,
        overallDailyCap: Double = 500,
        signatureStatGainPerLevel: Int = 3,
        otherStatGainPerLevel: Int = 1,
        conditionMultiplierFloor: Double = 0.2
    ) {
        self.dailyWakeXP = max(dailyWakeXP, 0)
        self.focusSessionXPPerMinute = max(focusSessionXPPerMinute, 0)
        self.focusSessionMinimumMinutes = max(focusSessionMinimumMinutes, 0)
        self.focusSessionDailyCap = max(focusSessionDailyCap, 0)
        self.careActionXP = max(careActionXP, 0)
        self.careActionDailyCap = max(careActionDailyCap, 0)
        self.taskCompletedXP = max(taskCompletedXP, 0)
        self.taskCompletedDailyCap = max(taskCompletedDailyCap, 0)
        self.milestoneXP = max(milestoneXP, 0)
        self.milestoneDailyCap = max(milestoneDailyCap, 0)
        self.workLoggedXP = max(workLoggedXP, 0)
        self.workLoggedDailyCap = max(workLoggedDailyCap, 0)
        self.overallDailyCap = max(overallDailyCap, 0)
        self.signatureStatGainPerLevel = max(signatureStatGainPerLevel, 0)
        self.otherStatGainPerLevel = max(otherStatGainPerLevel, 0)
        self.conditionMultiplierFloor = min(max(conditionMultiplierFloor, 0), 1)
    }

    public func dailyCap(for source: XPSource) -> Double {
        switch source {
        case .dailyWake: dailyWakeXP
        case .focusSession: focusSessionDailyCap
        case .care: careActionDailyCap
        case .taskCompleted: taskCompletedDailyCap
        case .milestone: milestoneDailyCap
        case .workLogged: workLoggedDailyCap
        }
    }
}

extension PetNeeds {
    /// XP accrues at a fraction of full rate when wellbeing is poor, floored
    /// so neglect slows progression without fully halting it — "learns
    /// slowly," not "can't learn at all."
    public func xpMultiplier(floor: Double) -> Double {
        let average = (fullness + energy + happiness + trust) / 4
        return max(floor, average / 100)
    }
}
