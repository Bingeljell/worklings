public enum PetPalette: String, Equatable, Sendable {
    case bright
    case calm
    case hungry
    case sleepy
    case sad
    case wary
}

public enum PetFace: String, Equatable, Sendable {
    case happy
    case neutral
    case hungry
    case sleepy
    case sad
    case wary
}

public enum CompanionTransitionKind: Equatable, Sendable {
    case reveal
    case conceal
    case familySwap
}

public struct CompanionTransitionFrame: Equatable, Sendable {
    public let spriteIndex: Int
    public let isPetVisible: Bool
    public let shouldSwapFamily: Bool

    public init(
        spriteIndex: Int,
        isPetVisible: Bool,
        shouldSwapFamily: Bool
    ) {
        self.spriteIndex = spriteIndex
        self.isPetVisible = isPetVisible
        self.shouldSwapFamily = shouldSwapFamily
    }
}

public enum CompanionTransitionPlan {
    public static let frameCount = 8
    public static let obscuringFrameIndex = 4

    public static func frames(
        for kind: CompanionTransitionKind
    ) -> [CompanionTransitionFrame] {
        (0..<frameCount).map { index in
            let isPetVisible = switch kind {
            case .reveal: index >= obscuringFrameIndex
            case .conceal: index < obscuringFrameIndex
            case .familySwap: true
            }

            return CompanionTransitionFrame(
                spriteIndex: index,
                isPetVisible: isPetVisible,
                shouldSwapFamily: kind == .familySwap
                    && index == obscuringFrameIndex
            )
        }
    }
}

public struct PetPresentation: Equatable, Sendable {
    public let moodLabel: String
    public let palette: PetPalette
    public let face: PetFace
    public let thought: String?

    public init(
        moodLabel: String,
        palette: PetPalette,
        face: PetFace,
        thought: String?
    ) {
        self.moodLabel = moodLabel
        self.palette = palette
        self.face = face
        self.thought = thought
    }

    /// The one place the level-and-class readout is formatted, so the care
    /// card, the menu-bar header, and accessibility labels can never drift
    /// into different spellings of the same fact.
    public static func levelClassLabel(for state: PetState) -> String {
        "Level \(state.level) \(state.petClass.displayName)"
    }

    /// Surfaces the condition multiplier — the care→XP coupling — as one plain
    /// line, so it stops being an invisible number players can only reverse-
    /// engineer from shrunken grants. Uses the same default floor the live
    /// brain runs with (`PetSession` never overrides `PetProgressionRates`), so
    /// the percentage shown is the rate XP is actually granted at.
    public static func learningRatePercent(for state: PetState) -> Int {
        let multiplier = state.needs.xpMultiplier(
            floor: PetProgressionRates().conditionMultiplierFloor
        )
        return Int((multiplier * 100).rounded())
    }

    public static func learningRateLabel(for state: PetState) -> String {
        "Learning at \(learningRatePercent(for: state))% — a happier Workling earns faster"
    }

    public static func make(
        state: PetState,
        reaction: PetReaction? = nil
    ) -> PetPresentation {
        let moodContent: PetPresentation

        switch state.mood {
        case .happy:
            moodContent = PetPresentation(
                moodLabel: "Happy",
                palette: .bright,
                face: .happy,
                thought: nil
            )
        case .content:
            moodContent = PetPresentation(
                moodLabel: "Content",
                palette: .calm,
                face: .neutral,
                thought: nil
            )
        case .hungry:
            moodContent = PetPresentation(
                moodLabel: "Hungry",
                palette: .hungry,
                face: .hungry,
                thought: "Snack time?"
            )
        case .sleepy:
            moodContent = PetPresentation(
                moodLabel: "Sleepy",
                palette: .sleepy,
                face: .sleepy,
                thought: "So sleepy…"
            )
        case .sad:
            moodContent = PetPresentation(
                moodLabel: "Sad",
                palette: .sad,
                face: .sad,
                thought: "Can we hang out?"
            )
        case .wary:
            moodContent = PetPresentation(
                moodLabel: "Wary",
                palette: .wary,
                face: .wary,
                thought: "I need some care."
            )
        }

        guard let reaction else {
            return moodContent
        }

        let reactionFace: PetFace = switch reaction {
        case .tooTiredToPlay: .sleepy
        case .sharedSetback, .noticedYouAreAway: .sad
        case .tookABreak, .waitingOnYou: .neutral
        default: .happy
        }

        return PetPresentation(
            moodLabel: moodContent.moodLabel,
            palette: moodContent.palette,
            face: reactionFace,
            thought: reaction.thought
        )
    }
}

private extension PetReaction {
    var thought: String {
        switch self {
        case .likedFood: "Tasty!"
        case .lovedFood: "My favourite!"
        case .enjoyedPlay: "That was fun!"
        case .lovedPlay: "Again, again!"
        case .comforted: "I like you."
        case .rested: "Much better."
        case .tooTiredToPlay: "Maybe after a nap…"
        case .happyToSeeYou: "A new day!"
        case .celebratedTask: "We did it!"
        case .sharedSetback: "We'll get the next one."
        case .proudOfMilestone: "Shipped!"
        case .gladYouAreBack: "You're back!"
        case .startedWorking: "Let's get to work!"
        case .tookABreak: "Taking a breather."
        case .waitingOnYou: "Waiting on you…"
        case .noticedYouAreAway: "Oh, you're away…"
        case .loggedWork: "Logged!"
        }
    }
}
