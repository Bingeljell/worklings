import AppKit
import Combine
import CompanionCore
import Foundation
import SwiftUI

/// Which side of the arena a bit of speech belongs above.
enum CombatSide {
    case pet
    case foe
}

/// Drives one encounter for the UI: steps the engine, reveals it a beat at a
/// time so it reads as a fight, pauses for decisions, and writes the result back
/// to the session on completion.
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
    /// The creature the current speech bubble sits above, and its short line —
    /// nil hides the bubbles.
    @Published private(set) var speaker: CombatSide?
    @Published private(set) var speechLine: String?

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
                    self.applySpeech(for: event)
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

    /// Updates the speech bubbles for a beat. Action beats put a short line above
    /// the acting creature; a decision or the ending clears them; structural
    /// beats (round markers) leave the last bubble in place.
    private func applySpeech(for event: CombatEvent) {
        switch event {
        case .decisionPoint, .encounterEnded:
            speaker = nil
            speechLine = nil
        case let .struck(attacker, _, outcome):
            let side: CombatSide = attacker == petName ? .pet : .foe
            speaker = side
            speechLine = outcome.didHit
                ? (outcome.didCrit ? "Critical! \(outcome.damage)!" : "\(outcome.damage)!")
                : "Miss!"
        case let .signature(attacker, _, outcome):
            speaker = attacker == petName ? .pet : .foe
            speechLine = "Signature! \(outcome.damage)!"
        case let .braced(who, _):
            speaker = who == petName ? .pet : .foe
            speechLine = "Bracing…"
        case let .defeated(who):
            if who == petName {
                speaker = .foe
                speechLine = "Gotcha!"
            } else {
                speaker = .pet
                speechLine = "Victory!"
            }
        case .encounterBegan, .roundBegan:
            break
        }
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
    private var onDismiss: (() -> Void)?

    var isPresenting: Bool { panel != nil }

    /// Opens the arena for one fight. `onDismiss` runs when it closes (however it
    /// closes), so the caller can bring the desktop companion back.
    func present(session: PetSession, foe: Foe, seed: UInt64, onDismiss: @escaping () -> Void) {
        dismiss()
        self.onDismiss = onDismiss

        let model = CombatViewModel(session: session, foe: foe, seed: seed)
        self.model = model

        let root = CombatPanelView(model: model, onClose: { [weak self] in self?.dismiss() })
        let hosting = NSHostingView(rootView: root)

        // No .closable — the in-panel Close/Return is the only exit, so the
        // dismiss path (and the companion's return) always runs.
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 600, height: 480),
            styleMask: [.titled, .nonactivatingPanel],
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
        let wasPresenting = panel != nil
        panel?.orderOut(nil)
        panel = nil
        model = nil
        if wasPresenting {
            let callback = onDismiss
            onDismiss = nil
            callback?()
        }
    }
}

// MARK: - View

struct CombatPanelView: View {
    @ObservedObject var model: CombatViewModel
    var onClose: () -> Void

    @State private var approach: Approach = .aggressive
    @State private var unleash = false

    private static let creatureSize: CGFloat = 150

    var body: some View {
        VStack(spacing: 0) {
            header
            arena
            controlBar
        }
        .frame(width: 600, height: 480)
        .background(stageBackground)
        .foregroundStyle(.white)
    }

    private var stageBackground: some View {
        LinearGradient(
            colors: [
                Color(red: 0.11, green: 0.10, blue: 0.17),
                Color(red: 0.05, green: 0.05, blue: 0.09)
            ],
            startPoint: .top, endPoint: .bottom
        )
    }

    private var header: some View {
        HStack {
            Text("The Cache Warren")
                .font(.title3.bold())
            Spacer()
            Button(action: onClose) {
                Image(systemName: "xmark.circle.fill").foregroundStyle(.white.opacity(0.55))
            }
            .buttonStyle(.plain)
            .help("Leave the delve")
        }
        .padding(.horizontal, 18)
        .padding(.top, 14)
    }

    private var arena: some View {
        HStack(alignment: .bottom, spacing: 0) {
            combatant(side: .pet, name: model.petName, hp: model.petHP, maxHP: model.petMaxHP, tint: .green)
            Spacer()
            Text("vs")
                .font(.system(.title3, design: .rounded).weight(.black))
                .foregroundStyle(.white.opacity(0.3))
                .padding(.bottom, 60)
            Spacer()
            combatant(side: .foe, name: model.foeName, hp: model.foeHP, maxHP: model.foeMaxHP, tint: .orange)
        }
        .padding(.horizontal, 34)
        .frame(maxHeight: .infinity)
    }

    private func combatant(side: CombatSide, name: String, hp: Int, maxHP: Int, tint: Color) -> some View {
        VStack(spacing: 8) {
            // Reserve the bubble's height so the creatures never shift.
            SpeechBubble(text: model.speaker == side ? model.speechLine : nil)
                .frame(height: 54)

            ZStack(alignment: .bottom) {
                Ellipse()
                    .fill(.black.opacity(0.25))
                    .frame(width: Self.creatureSize * 0.6, height: 14)
                    .blur(radius: 3)
                if side == .pet {
                    PetCombatSprite(family: model.petFamily, pose: model.petPose, size: Self.creatureSize)
                } else {
                    FoePlaceholder(size: Self.creatureSize)
                }
            }
            .frame(height: Self.creatureSize)

            VStack(spacing: 4) {
                Text(name).font(.headline)
                HPBar(fraction: maxHP > 0 ? Double(max(hp, 0)) / Double(maxHP) : 0, tint: tint)
                    .frame(width: 132)
                Text("\(max(hp, 0)) / \(maxHP)")
                    .font(.caption2).monospacedDigit()
                    .foregroundStyle(.white.opacity(0.65))
            }
        }
    }

    private var controlBar: some View {
        Group {
            if let outcome = model.outcome {
                summary(outcome)
            } else if model.awaitingDecision != nil {
                decisionControls
            } else {
                Label("The fight unfolds…", systemImage: "hourglass")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.55))
            }
        }
        .padding(.horizontal, 18)
        .padding(.bottom, 16)
        .frame(maxWidth: .infinity)
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

/// The pet in the arena: faces right toward the foe, blinks while idle, and
/// plays its action pose (with a small lunge on an attack) on the current beat.
private struct PetCombatSprite: View {
    let family: PetFamily
    let pose: WorklingSpriteFrame
    let size: CGFloat

    var body: some View {
        TimelineView(.periodic(from: .now, by: 0.7)) { context in
            WorklingSprite(family: family, frame: displayPose(at: context.date), size: size)
        }
        .scaleEffect(x: -1, y: 1) // sheets face left; flip to face the foe
        .offset(x: isLunging ? 24 : 0)
        .animation(.easeOut(duration: 0.14), value: pose)
    }

    private func displayPose(at date: Date) -> WorklingSpriteFrame {
        guard pose == .idle else { return pose }
        let phase = Int(date.timeIntervalSinceReferenceDate / 0.7)
        return phase.isMultiple(of: 2) ? .idle : .idleBlink
    }

    private var isLunging: Bool {
        pose == .strike || pose == .signature
    }
}

/// The foe's arena slot. Placeholder until the foe sprites land — it faces the
/// pet and bobs gently so the standoff still feels alive.
private struct FoePlaceholder: View {
    let size: CGFloat

    var body: some View {
        TimelineView(.periodic(from: .now, by: 0.9)) { context in
            let phase = Int(context.date.timeIntervalSinceReferenceDate / 0.9)
            Image(systemName: "ant.fill")
                .resizable().scaledToFit()
                .foregroundStyle(.orange)
                .padding(size * 0.22)
                .frame(width: size, height: size)
                .offset(y: phase.isMultiple(of: 2) ? 0 : -4)
                .animation(.easeInOut(duration: 0.45), value: phase)
        }
    }
}

/// A comic speech bubble with a downward tail. Empty when `text` is nil, but its
/// caller reserves the height so creatures never shift.
private struct SpeechBubble: View {
    let text: String?

    var body: some View {
        ZStack {
            if let text {
                VStack(spacing: 0) {
                    Text(text)
                        .font(.system(.headline, design: .rounded).weight(.semibold))
                        .foregroundStyle(.black)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 8)
                        .background(.white, in: RoundedRectangle(cornerRadius: 14))
                    BubbleTail()
                        .fill(.white)
                        .frame(width: 16, height: 9)
                }
                .transition(.scale(scale: 0.5).combined(with: .opacity))
                .id(text)
            }
        }
        .animation(.spring(response: 0.28, dampingFraction: 0.68), value: text)
    }
}

private struct BubbleTail: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.minX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.midX, y: rect.maxY))
        path.closeSubpath()
        return path
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
