import AppKit
import Combine
import CompanionCore
import Foundation
import SwiftUI

/// Drives one encounter for the UI: steps the engine, reveals the narration log
/// a beat at a time so it reads as a fight, pauses for decisions, and writes the
/// result back to the session on completion.
@MainActor
final class CombatViewModel: ObservableObject {
    @Published private(set) var lines: [String] = []
    @Published private(set) var petHP: Int
    @Published private(set) var petMaxHP: Int
    @Published private(set) var foeHP: Int
    @Published private(set) var foeMaxHP: Int
    @Published private(set) var awaitingDecision: DecisionReason?
    @Published private(set) var outcome: EncounterResolution?
    /// The pet's current pose, driven by the beat being revealed, so the sprite
    /// strikes, recoils, braces, and celebrates in time with the narration.
    @Published private(set) var petPose: WorklingSpriteFrame = .idle

    let petName: String
    let foeName: String
    let petFamily: PetFamily

    private let session: PetSession
    private let foe: Foe
    private var encounter: CombatEncounter
    private var revealIndex = 0
    private var pumpTask: Task<Void, Never>?

    /// Per-beat reveal delay, collapsed to nothing under Reduce Motion.
    private var beat: Duration {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
            ? .zero : .milliseconds(650)
    }

    init(session: PetSession, foe: Foe, approach: Approach = .aggressive, seed: UInt64) {
        self.session = session
        self.foe = foe
        let pet = session.makePetCombatant()
        self.encounter = CombatEncounter(
            pet: pet, foe: foe, approach: approach,
            rates: session.combatRates, seed: seed
        )
        petName = pet.name
        foeName = foe.name
        petFamily = session.state.family
        petHP = encounter.pet.currentHP
        petMaxHP = encounter.pet.maxHP
        foeHP = encounter.foe.currentHP
        foeMaxHP = foe.maxHP
        pump()
    }

    deinit { pumpTask?.cancel() }

    var signatureReady: Bool { encounter.signatureReady }

    func decide(approach: Approach, unleash: Bool) {
        guard awaitingDecision != nil else { return }
        awaitingDecision = nil
        encounter.decide(approach: approach, unleash: unleash)
        pump()
    }

    /// Reveals any queued log beats, then advances the engine — pausing at a
    /// decision and finishing at an ending.
    private func pump() {
        pumpTask?.cancel()
        pumpTask = Task { @MainActor [weak self] in
            while let self {
                while self.revealIndex < self.encounter.log.count {
                    let event = self.encounter.log[self.revealIndex]
                    self.revealIndex += 1
                    if let line = self.narrate(event) { self.lines.append(line) }
                    self.petPose = self.pose(for: event)
                    self.syncHP()
                    try? await Task.sleep(for: self.beat)
                    if Task.isCancelled { return }
                }
                switch self.encounter.status {
                case .ongoing:
                    self.encounter.step()
                case .awaitingDecision(let reason):
                    self.awaitingDecision = reason
                    return
                case .petVictory, .petDefeat:
                    self.finish()
                    return
                }
            }
        }
    }

    private func syncHP() {
        petHP = encounter.pet.currentHP
        foeHP = encounter.foe.currentHP
    }

    /// The pet's pose for the beat being revealed. Action beats (its own strike,
    /// a hit landing on it, bracing, unleashing, the ending) drive a matching
    /// pose; everything else rests on idle, or Low-HP once it's hurt enough.
    private func pose(for event: CombatEvent) -> WorklingSpriteFrame {
        switch event {
        case let .struck(attacker, _, outcome):
            if attacker == petName { return .strike }
            return outcome.didHit ? .hurt : restingPose
        case let .signature(attacker, _, _):
            return attacker == petName ? .signature : restingPose
        case let .braced(who, _):
            return who == petName ? .brace : restingPose
        case let .defeated(who):
            return who == petName ? .downed : restingPose
        case let .encounterEnded(victory):
            return victory ? .victory : .downed
        default:
            return restingPose
        }
    }

    private var restingPose: WorklingSpriteFrame {
        let fraction = petMaxHP > 0 ? Double(petHP) / Double(petMaxHP) : 1
        return fraction < session.combatRates.lowHPEventThreshold ? .lowHP : .idle
    }

    private func finish() {
        let resolution = session.state.applyingOutcome(
            of: encounter, foe: foe, rates: session.combatRates
        )
        session.applyCombatResolution(resolution)
        outcome = resolution
    }

    private func narrate(_ event: CombatEvent) -> String? {
        switch event {
        case let .encounterBegan(_, foe):
            return "A \(foe) blocks the way."
        case let .roundBegan(number):
            return "— Round \(number) —"
        case let .struck(attacker, defender, outcome):
            guard outcome.didHit else { return "\(attacker) strikes at \(defender) — a miss!" }
            let crit = outcome.didCrit ? " A critical hit!" : ""
            return "\(attacker) hits \(defender) for \(outcome.damage).\(crit)"
        case let .signature(attacker, defender, outcome):
            return "\(attacker) unleashes — \(outcome.damage) to \(defender)!"
        case let .braced(who, regen):
            return "\(who) braces, steadying (+\(regen))."
        case let .defeated(who):
            return "\(who) is defeated!"
        case let .decisionPoint(reason):
            return reason == .lowHP
                ? "\(petName) is faltering — what now?"
                : "A moment to reassess…"
        case .encounterEnded:
            return nil
        }
    }
}

// MARK: - Panel window

/// Hosts the combat panel in its own floating window for the length of a fight,
/// then tears it down. One fight at a time.
@MainActor
final class CombatPanelController {
    private var panel: NSPanel?
    private var model: CombatViewModel?

    var isPresenting: Bool { panel != nil }

    func present(session: PetSession, foe: Foe, seed: UInt64) {
        dismiss()

        let model = CombatViewModel(session: session, foe: foe, seed: seed)
        self.model = model

        let root = CombatPanelView(model: model, onClose: { [weak self] in self?.dismiss() })
        let hosting = NSHostingView(rootView: root)

        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 440),
            styleMask: [.titled, .closable, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.title = "The Cache Warren"
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.contentView = hosting
        panel.setContentSize(hosting.fittingSize)
        panel.center()
        panel.makeKeyAndOrderFront(nil)
        self.panel = panel
    }

    func dismiss() {
        panel?.orderOut(nil)
        panel = nil
        model = nil
    }
}

// MARK: - View

struct CombatPanelView: View {
    @ObservedObject var model: CombatViewModel
    var onClose: () -> Void

    @State private var approach: Approach = .aggressive
    @State private var unleash = false

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            CombatantRow(
                name: model.petName,
                hp: model.petHP, maxHP: model.petMaxHP, tint: .green
            ) {
                WorklingSprite(family: model.petFamily, frame: model.petPose, size: 60)
            }
            CombatantRow(
                name: model.foeName,
                hp: model.foeHP, maxHP: model.foeMaxHP, tint: .orange
            ) {
                // Placeholder — foe sprites are a later asset drop.
                Image(systemName: "ant.fill")
                    .font(.system(size: 30))
                    .foregroundStyle(.orange)
            }
            logView
            Divider().opacity(0.3)
            controls
        }
        .padding(18)
        .frame(width: 340)
        .background(Color(white: 0.12))
        .foregroundStyle(.white)
    }

    private var header: some View {
        HStack {
            Text("The Cache Warren").font(.title3.bold())
            Spacer()
            Button(action: onClose) {
                Image(systemName: "xmark.circle.fill").foregroundStyle(.white.opacity(0.6))
            }
            .buttonStyle(.plain)
            .help("Leave the delve")
        }
    }

    private var logView: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 4) {
                    ForEach(Array(model.lines.enumerated()), id: \.offset) { index, line in
                        Text(line)
                            .font(.system(.callout, design: .rounded))
                            .foregroundStyle(.white.opacity(0.85))
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .id(index)
                    }
                }
            }
            .frame(height: 120)
            .onChange(of: model.lines.count) { _, count in
                withAnimation { proxy.scrollTo(count - 1, anchor: .bottom) }
            }
        }
    }

    @ViewBuilder
    private var controls: some View {
        if let outcome = model.outcome {
            summary(outcome)
        } else if model.awaitingDecision != nil {
            decisionControls
        } else {
            Label("The fight unfolds…", systemImage: "hourglass")
                .font(.caption)
                .foregroundStyle(.white.opacity(0.6))
        }
    }

    private var decisionControls: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Your move").font(.caption.bold()).foregroundStyle(.white.opacity(0.7))
            Picker("Approach", selection: $approach) {
                ForEach(Approach.allCases, id: \.self) { option in
                    Text(name(for: option)).tag(option)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            if model.signatureReady {
                Toggle("Unleash the Signature", isOn: $unleash)
                    .font(.caption)
                    .toggleStyle(.checkbox)
            }

            Button {
                model.decide(approach: approach, unleash: unleash)
                unleash = false
            } label: {
                Text("Continue").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
        }
    }

    private func summary(_ resolution: EncounterResolution) -> some View {
        let won = resolution.tier != .downed
        return VStack(alignment: .leading, spacing: 8) {
            Text(won ? "Victory!" : "Downed…")
                .font(.title3.bold())
                .foregroundStyle(won ? .green : .orange)
            Text("\(tierName(resolution.tier)) — \(exitBlurb(resolution.tier))")
                .font(.caption)
                .foregroundStyle(.white.opacity(0.75))
            if resolution.xpGained > 0 {
                Label("+\(Int(resolution.xpGained)) XP", systemImage: "sparkles")
                    .font(.caption)
            }
            Button {
                onClose()
            } label: {
                Text("Return").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
        }
    }

    private func name(for approach: Approach) -> String {
        switch approach {
        case .aggressive: "Aggressive"
        case .careful: "Careful"
        case .clever: "Clever"
        }
    }

    private func tierName(_ tier: ExitTier) -> String {
        switch tier {
        case .flawless: "Flawless"
        case .solid: "Solid"
        case .barely: "Barely"
        case .downed: "Downed"
        }
    }

    private func exitBlurb(_ tier: ExitTier) -> String {
        switch tier {
        case .flawless: "you return triumphant"
        case .solid: "a fair fight, a little worn"
        case .barely: "you limp back, shaken"
        case .downed: "you retreat to recover"
        }
    }
}

private struct CombatantRow<Avatar: View>: View {
    let name: String
    let hp: Int
    let maxHP: Int
    let tint: Color
    @ViewBuilder var avatar: () -> Avatar

    var body: some View {
        HStack(spacing: 12) {
            avatar()
                .frame(width: 60, height: 60)
                .background(RoundedRectangle(cornerRadius: 10).fill(.white.opacity(0.08)))
            VStack(alignment: .leading, spacing: 5) {
                HStack {
                    Text(name).font(.headline)
                    Spacer()
                    Text("\(max(hp, 0))/\(maxHP)").font(.caption).monospacedDigit()
                        .foregroundStyle(.white.opacity(0.7))
                }
                HPBar(fraction: maxHP > 0 ? Double(max(hp, 0)) / Double(maxHP) : 0, tint: tint)
            }
        }
    }
}

private struct HPBar: View {
    let fraction: Double
    let tint: Color

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(0.15))
                Capsule().fill(tint)
                    .frame(width: geo.size.width * CGFloat(min(max(fraction, 0), 1)))
            }
        }
        .frame(height: 8)
    }
}
