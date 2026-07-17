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

        return PetPresentation(
            moodLabel: moodContent.moodLabel,
            palette: moodContent.palette,
            face: reaction == .tooTiredToPlay ? .sleepy : .happy,
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
        }
    }
}
