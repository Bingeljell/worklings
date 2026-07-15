import CompanionCore
import Foundation

enum PetPresentationChecks {
    static func run(context: inout CheckContext) {
        checkContentPresentation(context: &context)
        checkHappyPresentationIsQuiet(context: &context)
        checkNeedPresentations(context: &context)
        checkReactionOverride(context: &context)
        checkTiredReaction(context: &context)
    }

    private static func checkHappyPresentationIsQuiet(context: inout CheckContext) {
        let presentation = PetPresentation.make(
            state: makeState(
                needs: PetNeeds(hunger: 10, energy: 80, happiness: 90, trust: 80)
            )
        )

        context.expectEqual(presentation.moodLabel, "Happy", "happy mood has label")
        context.expectEqual(presentation.thought, nil, "happy mood does not show persistent bubble")
    }

    private static func checkContentPresentation(context: inout CheckContext) {
        let presentation = PetPresentation.make(
            state: makeState(
                needs: PetNeeds(hunger: 20, energy: 70, happiness: 60, trust: 50)
            )
        )

        context.expectEqual(presentation.moodLabel, "Content", "content mood has label")
        context.expectEqual(presentation.palette, .calm, "content mood uses calm palette")
        context.expectEqual(presentation.face, .neutral, "content mood uses neutral face")
        context.expectEqual(presentation.thought, nil, "content mood has no persistent bubble")
    }

    private static func checkNeedPresentations(context: inout CheckContext) {
        let hungry = PetPresentation.make(
            state: makeState(
                needs: PetNeeds(hunger: 90, energy: 80, happiness: 70, trust: 50)
            )
        )
        let sleepy = PetPresentation.make(
            state: makeState(
                needs: PetNeeds(hunger: 20, energy: 10, happiness: 70, trust: 50)
            )
        )
        let wary = PetPresentation.make(
            state: makeState(
                needs: PetNeeds(hunger: 20, energy: 80, happiness: 70, trust: 10)
            )
        )

        context.expectEqual(hungry.palette, .hungry, "hungry mood uses hungry palette")
        context.expectEqual(hungry.face, .hungry, "hungry mood uses hungry face")
        context.expectEqual(sleepy.palette, .sleepy, "sleepy mood uses sleepy palette")
        context.expectEqual(sleepy.face, .sleepy, "sleepy mood uses sleepy face")
        context.expectEqual(wary.palette, .wary, "wary mood uses wary palette")
        context.expectEqual(wary.face, .wary, "wary mood uses wary face")
    }

    private static func checkReactionOverride(context: inout CheckContext) {
        let presentation = PetPresentation.make(
            state: makeState(
                needs: PetNeeds(hunger: 90, energy: 80, happiness: 70, trust: 50)
            ),
            reaction: .lovedFood
        )

        context.expectEqual(
            presentation.thought,
            "My favourite!",
            "interaction temporarily overrides need thought"
        )
        context.expectEqual(presentation.face, .happy, "positive interaction uses happy face")
        context.expectEqual(
            presentation.palette,
            .hungry,
            "reaction preserves underlying mood palette"
        )
    }

    private static func checkTiredReaction(context: inout CheckContext) {
        let presentation = PetPresentation.make(
            state: makeState(),
            reaction: .tooTiredToPlay
        )

        context.expectEqual(presentation.face, .sleepy, "tired refusal uses sleepy face")
        context.expectEqual(
            presentation.thought,
            "Maybe after a nap…",
            "tired refusal explains the reaction"
        )
    }

    private static func makeState(
        needs: PetNeeds = PetNeeds(hunger: 15, energy: 80, happiness: 70, trust: 50)
    ) -> PetState {
        PetState(
            name: "Pixel",
            needs: needs,
            preferences: PetPreferences(
                favouriteFood: .berries,
                favouritePlayActivity: .puzzle
            ),
            lastUpdatedAt: Date(timeIntervalSinceReferenceDate: 0)
        )
    }
}
