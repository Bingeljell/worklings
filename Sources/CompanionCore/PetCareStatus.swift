public enum PetUrgency: Int, Comparable, Equatable, Sendable {
    case none
    case notice
    case urgent
    case critical

    public static func < (lhs: PetUrgency, rhs: PetUrgency) -> Bool {
        lhs.rawValue < rhs.rawValue
    }
}

public enum PetNeedKind: String, CaseIterable, Equatable, Sendable {
    case hunger
    case energy
    case happiness
    case trust

    public var displayName: String {
        switch self {
        case .hunger: "Hunger"
        case .energy: "Energy"
        case .happiness: "Happiness"
        case .trust: "Trust"
        }
    }

    fileprivate var isPhysical: Bool {
        self == .hunger || self == .energy
    }

    fileprivate var stableOrder: Int {
        switch self {
        case .hunger: 0
        case .energy: 1
        case .trust: 2
        case .happiness: 3
        }
    }
}

public struct PetNeedCondition: Equatable, Sendable {
    public let kind: PetNeedKind
    public let urgency: PetUrgency
    public let value: Double
    public let phrase: String

    public init(
        kind: PetNeedKind,
        urgency: PetUrgency,
        value: Double,
        phrase: String
    ) {
        self.kind = kind
        self.urgency = urgency
        self.value = value
        self.phrase = phrase
    }
}

public enum PetCareActionKind: String, CaseIterable, Equatable, Sendable {
    case feed
    case play
    case pet
    case sleep
}

public struct PetActionAvailability: Equatable, Sendable {
    public let isEnabled: Bool
    public let explanation: String?

    public init(isEnabled: Bool, explanation: String? = nil) {
        self.isEnabled = isEnabled
        self.explanation = explanation
    }
}

public struct PetCareStatus: Equatable, Sendable {
    public let conditions: [PetNeedCondition]
    public let hoverSummary: String

    public init(conditions: [PetNeedCondition], hoverSummary: String) {
        self.conditions = conditions
        self.hoverSummary = hoverSummary
    }

    public static func make(state: PetState) -> PetCareStatus {
        let conditions = [
            hungerCondition(state.needs.hunger),
            energyCondition(state.needs.energy),
            happinessCondition(state.needs.happiness),
            trustCondition(state.needs.trust)
        ]
        .compactMap { $0 }
        .sorted(by: conditionComesFirst)

        let shownConditions = conditions.prefix(2)
        let hoverSummary: String

        switch shownConditions.count {
        case 0:
            hoverSummary = state.mood == .happy
                ? "\(state.name) is happy."
                : "\(state.name) is doing well."
        case 1:
            hoverSummary = "\(state.name) is \(shownConditions[shownConditions.startIndex].phrase)."
        default:
            let firstIndex = shownConditions.startIndex
            let secondIndex = shownConditions.index(after: firstIndex)
            hoverSummary = "\(state.name) is \(shownConditions[firstIndex].phrase) and \(shownConditions[secondIndex].phrase)."
        }

        return PetCareStatus(
            conditions: conditions,
            hoverSummary: hoverSummary
        )
    }

    public var ambientCondition: PetNeedCondition? {
        conditions.first { $0.urgency >= .urgent }
    }

    public func availability(
        for action: PetCareActionKind,
        state: PetState
    ) -> PetActionAvailability {
        switch action {
        case .feed:
            guard state.needs.hunger > 0 else {
                return PetActionAvailability(
                    isEnabled: false,
                    explanation: "\(state.name) is already full."
                )
            }
        case .play:
            guard state.needs.energy >= 15 else {
                return PetActionAvailability(
                    isEnabled: false,
                    explanation: "\(state.name) needs a nap first."
                )
            }
        case .sleep:
            guard state.needs.energy < 100 else {
                return PetActionAvailability(
                    isEnabled: false,
                    explanation: "\(state.name) is fully rested."
                )
            }
        case .pet:
            break
        }

        return PetActionAvailability(isEnabled: true)
    }

    private static func hungerCondition(_ value: Double) -> PetNeedCondition? {
        switch value {
        case 90...:
            PetNeedCondition(kind: .hunger, urgency: .critical, value: value, phrase: "very hungry")
        case 75...:
            PetNeedCondition(kind: .hunger, urgency: .urgent, value: value, phrase: "hungry")
        case 55...:
            PetNeedCondition(kind: .hunger, urgency: .notice, value: value, phrase: "a little hungry")
        default:
            nil
        }
    }

    private static func energyCondition(_ value: Double) -> PetNeedCondition? {
        switch value {
        case ...10:
            PetNeedCondition(kind: .energy, urgency: .critical, value: value, phrase: "exhausted")
        case ...20:
            PetNeedCondition(kind: .energy, urgency: .urgent, value: value, phrase: "sleepy")
        case ...45:
            PetNeedCondition(kind: .energy, urgency: .notice, value: value, phrase: "getting tired")
        default:
            nil
        }
    }

    private static func happinessCondition(_ value: Double) -> PetNeedCondition? {
        switch value {
        case ...15:
            PetNeedCondition(kind: .happiness, urgency: .critical, value: value, phrase: "very unhappy")
        case ...30:
            PetNeedCondition(kind: .happiness, urgency: .urgent, value: value, phrase: "sad")
        case ...45:
            PetNeedCondition(kind: .happiness, urgency: .notice, value: value, phrase: "a little lonely")
        default:
            nil
        }
    }

    private static func trustCondition(_ value: Double) -> PetNeedCondition? {
        switch value {
        case ...10:
            PetNeedCondition(
                kind: .trust,
                urgency: .critical,
                value: value,
                phrase: "in need of reassurance"
            )
        case ...20:
            PetNeedCondition(kind: .trust, urgency: .urgent, value: value, phrase: "wary")
        case ...35:
            PetNeedCondition(kind: .trust, urgency: .notice, value: value, phrase: "a little unsure")
        default:
            nil
        }
    }

    private static func conditionComesFirst(
        _ lhs: PetNeedCondition,
        _ rhs: PetNeedCondition
    ) -> Bool {
        let lhsRank = priorityRank(for: lhs)
        let rhsRank = priorityRank(for: rhs)

        if lhsRank != rhsRank {
            return lhsRank < rhsRank
        }
        return lhs.kind.stableOrder < rhs.kind.stableOrder
    }

    private static func priorityRank(for condition: PetNeedCondition) -> Int {
        switch condition.urgency {
        case .critical:
            condition.kind.isPhysical ? 0 : 1
        case .urgent:
            condition.kind.isPhysical ? 2 : 3
        case .notice:
            condition.kind.isPhysical ? 4 : 5
        case .none:
            6
        }
    }
}
