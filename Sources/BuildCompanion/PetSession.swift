import Combine
import CompanionCore
import Foundation

@MainActor
final class PetSession: ObservableObject {
    @Published private(set) var state: PetState
    @Published private(set) var reaction: PetReaction?
    @Published private(set) var persistenceWarning: String?

    private let brain: PetBrain
    private let store: PetStateFileStore
    private let persistenceEnabled: Bool
    private var tickTask: Task<Void, Never>?
    private var reactionTask: Task<Void, Never>?

    init(now: Date = Date()) {
        brain = PetBrain()
        store = Self.makeDefaultStore()

        let initialState: PetState
        let canPersist: Bool
        let warning: String?

        do {
            initialState = try store.load() ?? PetState.newPet(now: now)
            canPersist = true
            warning = nil
        } catch {
            initialState = PetState.newPet(now: now)
            canPersist = false
            warning = "Saved state could not be read; it has been preserved."
            NSLog("Build Companion could not load pet state: %@", String(describing: error))
        }

        state = brain.advance(initialState, to: now)
        reaction = nil
        persistenceWarning = warning
        persistenceEnabled = canPersist

        persist()
        startTicking()
    }

    deinit {
        tickTask?.cancel()
        reactionTask?.cancel()
    }

    var careStatus: PetCareStatus {
        PetCareStatus.make(state: state)
    }

    func advance(to now: Date = Date()) {
        let nextState = brain.advance(state, to: now)
        guard nextState != state else {
            return
        }

        state = nextState
        persist()
    }

    func perform(_ action: PetAction, at now: Date = Date()) {
        let actionKind: PetCareActionKind
        switch action {
        case .feed:
            actionKind = .feed
        case .play:
            actionKind = .play
        case .pet:
            actionKind = .pet
        case .sleep:
            actionKind = .sleep
        }

        guard careStatus.availability(for: actionKind, state: state).isEnabled else {
            return
        }

        let result = brain.perform(action, on: state, at: now)
        state = result.state
        reaction = result.reaction
        persist()
        scheduleReactionClear(result.reaction)
    }

    private func persist() {
        guard persistenceEnabled else {
            return
        }

        do {
            try store.save(state)
            persistenceWarning = nil
        } catch {
            persistenceWarning = "Pet state could not be saved."
            NSLog("Build Companion could not save pet state: %@", String(describing: error))
        }
    }

    private func startTicking() {
        tickTask = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(60))
                guard !Task.isCancelled else {
                    return
                }
                self?.advance()
            }
        }
    }

    private func scheduleReactionClear(_ expectedReaction: PetReaction) {
        reactionTask?.cancel()
        reactionTask = Task { @MainActor [weak self] in
            try? await Task.sleep(for: .seconds(3))
            guard !Task.isCancelled, self?.reaction == expectedReaction else {
                return
            }
            self?.reaction = nil
        }
    }

    private static func makeDefaultStore() -> PetStateFileStore {
        let applicationSupportURL = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first ?? FileManager.default.temporaryDirectory

        return PetStateFileStore(
            fileURL: applicationSupportURL
                .appendingPathComponent("BuildCompanion", isDirectory: true)
                .appendingPathComponent("pet-state.json", isDirectory: false)
        )
    }
}
