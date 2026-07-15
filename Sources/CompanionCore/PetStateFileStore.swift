import Foundation

public enum PetStateFileStoreError: Error, Equatable, Sendable {
    case unsupportedSchema(found: Int, supported: Int)
}

public struct PetStateFileStore: Sendable {
    public let fileURL: URL

    public init(fileURL: URL) {
        self.fileURL = fileURL
    }

    public func load() throws -> PetState? {
        guard FileManager.default.fileExists(atPath: fileURL.path) else {
            return nil
        }

        let data = try Data(contentsOf: fileURL)
        let state = try JSONDecoder().decode(PetState.self, from: data)

        guard state.schemaVersion == PetState.currentSchemaVersion else {
            throw PetStateFileStoreError.unsupportedSchema(
                found: state.schemaVersion,
                supported: PetState.currentSchemaVersion
            )
        }

        return state
    }

    public func save(_ state: PetState) throws {
        let directoryURL = fileURL.deletingLastPathComponent()
        try FileManager.default.createDirectory(
            at: directoryURL,
            withIntermediateDirectories: true
        )

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(state)
        try data.write(to: fileURL, options: .atomic)
    }
}
