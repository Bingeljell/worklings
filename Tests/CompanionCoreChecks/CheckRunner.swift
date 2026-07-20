import Darwin

@main
enum CheckRunner {
    static func main() {
        var context = CheckContext()

        ScreenPlacementChecks.run(context: &context)
        PetBrainChecks.run(context: &context)
        ActivityChecks.run(context: &context)
        ActivitySourceChecks.run(context: &context)
        WorkLogChecks.run(context: &context)
        PetPersistenceChecks.run(context: &context)
        PetPresentationChecks.run(context: &context)
        PetCareStatusChecks.run(context: &context)

        guard context.failures.isEmpty else {
            for failure in context.failures {
                fputs("FAIL: \(failure)\n", stderr)
            }
            fputs(
                "CompanionCore checks failed (\(context.failures.count) failures)\n",
                stderr
            )
            exit(EXIT_FAILURE)
        }

        print("CompanionCore checks passed (\(context.passedCount)/\(context.passedCount))")
    }
}
