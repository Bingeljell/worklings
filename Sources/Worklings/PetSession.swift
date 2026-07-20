import Combine
import CompanionCore
import Foundation

@MainActor
final class PetSession: ObservableObject {
    @Published private(set) var state: PetState
    @Published private(set) var reaction: PetReaction?
    @Published private(set) var persistenceWarning: String?
    @Published private(set) var activityContext: ActivityContext = .quiet

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
            NSLog("Worklings could not load pet state: %@", String(describing: error))
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
        let currentContext = activityContext.expiring(at: now)
        if currentContext != activityContext {
            activityContext = currentContext
        }

        let nextState = brain.advance(state, to: now, context: currentContext)
        guard nextState != state else {
            return
        }

        state = nextState
        persist()
    }

    func receive(_ event: ActivityEvent, at now: Date = Date()) {
        activityContext = activityContext.reducing(event)

        let response = brain.observe(event, on: state, at: now)
        if response.state != state {
            state = response.state
            persist()
        }

        if let eventReaction = response.reaction {
            reaction = eventReaction
            scheduleReactionClear(eventReaction)
        }
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

    func selectFamily(_ family: PetFamily) {
        guard family != state.family else {
            return
        }

        state = state.selectingFamily(family)
        persist()
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
            NSLog("Worklings could not save pet state: %@", String(describing: error))
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
        let fileManager = FileManager.default
        let applicationSupportURL = fileManager.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first ?? fileManager.temporaryDirectory

        let stateFileName = "pet-state.json"
        let stateURL = applicationSupportURL
            .appendingPathComponent("Worklings", isDirectory: true)
            .appendingPathComponent(stateFileName, isDirectory: false)
        let legacyStateURL = applicationSupportURL
            .appendingPathComponent("BuildCompanion", isDirectory: true)
            .appendingPathComponent(stateFileName, isDirectory: false)

        if !fileManager.fileExists(atPath: stateURL.path),
           fileManager.fileExists(atPath: legacyStateURL.path) {
            do {
                try fileManager.createDirectory(
                    at: stateURL.deletingLastPathComponent(),
                    withIntermediateDirectories: true
                )
                try fileManager.copyItem(at: legacyStateURL, to: stateURL)
                NSLog("Worklings copied pet state from the legacy application directory.")
            } catch {
                NSLog("Worklings could not copy legacy pet state: %@", String(describing: error))
                return PetStateFileStore(fileURL: legacyStateURL)
            }
        }

        return PetStateFileStore(fileURL: stateURL)
    }
}
