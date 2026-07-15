import CompanionCore
import Foundation

enum PetPersistenceChecks {
    static func run(context: inout CheckContext) {
        let directoryURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("build-companion-checks-\(UUID().uuidString)")
        defer {
            try? FileManager.default.removeItem(at: directoryURL)
        }

        let store = PetStateFileStore(
            fileURL: directoryURL.appendingPathComponent("pet-state.json")
        )

        checkMissingFile(store: store, context: &context)
        checkRoundTrip(store: store, context: &context)
        checkUnsupportedSchema(store: store, context: &context)
        checkCorruptFilePreservation(store: store, context: &context)
    }

    private static func checkMissingFile(
        store: PetStateFileStore,
        context: inout CheckContext
    ) {
        do {
            let state = try store.load()
            context.expectEqual(state, nil, "missing save returns no state")
        } catch {
            context.expect(false, "missing save should not throw: \(error)")
        }
    }

    private static func checkRoundTrip(
        store: PetStateFileStore,
        context: inout CheckContext
    ) {
        let state = PetState.newPet(
            now: Date(timeIntervalSinceReferenceDate: 10_000.25)
        )

        do {
            try store.save(state)
            let loadedState = try store.load()
            context.expectEqual(loadedState, state, "saved state round trips through JSON")
        } catch {
            context.expect(false, "state round trip should succeed: \(error)")
        }
    }

    private static func checkUnsupportedSchema(
        store: PetStateFileStore,
        context: inout CheckContext
    ) {
        let unsupported = PetState(
            schemaVersion: 999,
            name: "Future Pixel",
            needs: PetNeeds(hunger: 10, energy: 80, happiness: 70, trust: 50),
            preferences: PetPreferences(
                favouriteFood: .berries,
                favouritePlayActivity: .puzzle
            ),
            lastUpdatedAt: Date(timeIntervalSinceReferenceDate: 11_000)
        )

        do {
            try store.save(unsupported)
        } catch {
            context.expect(false, "unsupported fixture should save before load check: \(error)")
            return
        }

        do {
            _ = try store.load()
            context.expect(false, "unsupported schema should fail loading")
        } catch let error as PetStateFileStoreError {
            context.expectEqual(
                error,
                .unsupportedSchema(found: 999, supported: 1),
                "unsupported schema reports found and supported versions"
            )
        } catch {
            context.expect(false, "unsupported schema returned unexpected error: \(error)")
        }
    }

    private static func checkCorruptFilePreservation(
        store: PetStateFileStore,
        context: inout CheckContext
    ) {
        let corruptData = Data("not valid pet JSON".utf8)

        do {
            try corruptData.write(to: store.fileURL, options: .atomic)
        } catch {
            context.expect(false, "corrupt fixture should be writable: \(error)")
            return
        }

        context.expectThrows("corrupt save fails to decode") {
            _ = try store.load()
        }

        do {
            let preservedData = try Data(contentsOf: store.fileURL)
            context.expectEqual(
                preservedData,
                corruptData,
                "failed load preserves corrupt save bytes"
            )
        } catch {
            context.expect(false, "corrupt save should remain readable as bytes: \(error)")
        }
    }
}
