struct CheckContext {
    private(set) var passedCount = 0
    private(set) var failures: [String] = []

    mutating func expect(_ condition: Bool, _ description: String) {
        if condition {
            passedCount += 1
        } else {
            failures.append(description)
        }
    }

    mutating func expectEqual<Value: Equatable>(
        _ actual: Value,
        _ expected: Value,
        _ description: String
    ) {
        expect(
            actual == expected,
            "\(description); expected \(expected), received \(actual)"
        )
    }

    mutating func expectApproximatelyEqual(
        _ actual: Double,
        _ expected: Double,
        accuracy: Double = 0.000_1,
        _ description: String
    ) {
        expect(
            abs(actual - expected) <= accuracy,
            "\(description); expected \(expected), received \(actual)"
        )
    }

    mutating func expectThrows(
        _ description: String,
        operation: () throws -> Void
    ) {
        do {
            try operation()
            failures.append("\(description); expected an error")
        } catch {
            passedCount += 1
        }
    }
}
