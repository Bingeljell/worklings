import CompanionCore
import Foundation

enum PetPersistenceChecks {
    static func run(context: inout CheckContext) {
        let directoryURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("worklings-checks-\(UUID().uuidString)")
        defer {
            try? FileManager.default.removeItem(at: directoryURL)
        }

        let store = PetStateFileStore(
            fileURL: directoryURL.appendingPathComponent("pet-state.json")
        )

        checkMissingFile(store: store, context: &context)
        checkRoundTrip(store: store, context: &context)
        checkDecodedNeedClamping(store: store, context: &context)
        checkUnsupportedSchema(store: store, context: &context)
        checkCorruptFilePreservation(store: store, context: &context)
    }

    private static func checkDecodedNeedClamping(
        store: PetStateFileStore,
        context: inout CheckContext
    ) {
        let json = """
        {
          "lastUpdatedAt": 0,
          "name": "Pixel",
          "needs": {
            "energy": 140,
            "happiness": -30,
            "hunger": -20,
            "trust": 500
          },
          "preferences": {
            "favouriteFood": "berries",
            "favouritePlayActivity": "puzzle"
          },
          "schemaVersion": 1
        }
        """

        do {
            try Data(json.utf8).write(to: store.fileURL, options: .atomic)
            guard let state = try store.load() else {
                context.expect(false, "out-of-range fixture should decode")
                return
            }
            context.expectEqual(
                state.needs,
                PetNeeds(hunger: 0, energy: 100, happiness: 0, trust: 100),
                "decoded needs are clamped through the domain initializer"
            )
        } catch {
            context.expect(false, "out-of-range fixture should load safely: \(error)")
        }
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

            let data = try Data(contentsOf: store.fileURL)
            let object = try JSONSerialization.jsonObject(with: data)
            let root = object as? [String: Any]
            let needs = root?["needs"] as? [String: Any]
            context.expect(
                needs?["fullness"] == nil,
                "derived fullness is not persisted"
            )
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
