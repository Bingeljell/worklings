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

/// The foe's arena pose. Foes have a small, fixed set of sprites (idle / attack
/// / hurt) rather than the pet's full sheet.
enum FoePose {
    case idle
    case attack
    case hurt
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
    /// The foe's current pose, driven the same way.
    @Published private(set) var foePose: FoePose = .idle
    /// Transient hit signals for the impact "juice": a token that ticks each time
    /// a side takes damage, plus that hit's amount and whether it crit. The view
    /// watches the token to fire a shake / flash / damage-number.
    @Published private(set) var petHitToken = 0
    @Published private(set) var petHitAmount = 0
    @Published private(set) var petHitCrit = false
    @Published private(set) var foeHitToken = 0
    @Published private(set) var foeHitAmount = 0
    @Published private(set) var foeHitCrit = false
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
    private var pumpTask: Task<Void, Never>?

    /// UI beats waiting to play, and how far through the engine log they've been
    /// turned into beats.
    private var pendingBeats: [Beat] = []
    private var processedEventIndex = 0

    /// One readable moment on screen: a line above a creature, the pet's pose, an
    /// optional HP change, and how long to hold before the next.
    private struct Beat {
        var side: CombatSide?
        var text: String?
        var petPose: WorklingSpriteFrame = .idle
        var foePose: FoePose = .idle
        var isCrit = false
        var hpChange: (side: CombatSide, amount: Int)?
        var hold: Duration
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
        speaker = nil
        speechLine = nil
        encounter.decide(approach: approach, unleash: unleash)
        pump()
    }

    /// Plays queued beats with their holds, stepping the engine for more, pausing
    /// at a decision, and finishing at an ending.
    private func pump() {
        pumpTask?.cancel()
        pumpTask = Task { @MainActor [weak self] in
            while let self {
                while self.pendingBeats.isEmpty {
                    switch self.encounter.status {
                    case .ongoing:
                        self.encounter.step()
                        self.enqueueNewBeats()
                    case .awaitingDecision(let reason):
                        self.showDecision(reason)
                        return
                    case .petVictory, .petDefeat:
                        self.finish()
                        return
                    }
                }
                let beat = self.pendingBeats.removeFirst()
                self.apply(beat)
                try? await Task.sleep(for: beat.hold)
                if Task.isCancelled { return }
            }
        }
    }

    /// Turns any freshly-appended engine events into UI beats.
    private func enqueueNewBeats() {
        while processedEventIndex < encounter.log.count {
            let event = encounter.log[processedEventIndex]
            processedEventIndex += 1
            pendingBeats.append(contentsOf: beats(for: event))
        }
    }

    private func apply(_ beat: Beat) {
        speaker = beat.side
        speechLine = beat.text
        foePose = beat.foePose
        // A stored `.idle` means "rest" — resolve it to Low-HP when hurt enough.
        petPose = beat.petPose == .idle ? restingPose : beat.petPose
        if let change = beat.hpChange {
            switch change.side {
            case .pet: petHP = min(petMaxHP, max(0, petHP + change.amount))
            case .foe: foeHP = min(foeMaxHP, max(0, foeHP + change.amount))
            }
            if change.amount < 0 {
                let amount = -change.amount
                switch change.side {
                case .pet:
                    petHitAmount = amount
                    petHitCrit = beat.isCrit
                    petHitToken += 1
                case .foe:
                    foeHitAmount = amount
                    foeHitCrit = beat.isCrit
                    foeHitToken += 1
                }
            }
        }
        if let text = beat.text { lines.append(text) }
    }

    private func showDecision(_ reason: DecisionReason) {
        awaitingDecision = reason
        speaker = .pet
        speechLine = reason == .lowHP ? "I'm hurting — what now?" : "What's the plan?"
    }

    /// Expands one engine event into the readable beats it plays. A strike becomes
    /// a wind-up ("… attacks the …") then a result ("… hits … for N damage"), each
    /// held long enough to read; the foe's own wind-up is its "gearing up" beat.
    private func beats(for event: CombatEvent) -> [Beat] {
        switch event {
        case let .encounterBegan(_, foe):
            return [Beat(side: .foe, text: "A \(foe) blocks the way!", hold: .milliseconds(1300))]

        case let .struck(attacker, defender, outcome):
            let attackerSide: CombatSide = attacker == petName ? .pet : .foe
            let defenderSide: CombatSide = defender == petName ? .pet : .foe
            let petAttacking = attackerSide == .pet
            let windupPetPose: WorklingSpriteFrame = petAttacking ? .strike : .idle
            let windupFoePose: FoePose = petAttacking ? .idle : .attack
            var result = [
                Beat(
                    side: attackerSide,
                    text: "\(attacker) attacks the \(defender).",
                    petPose: windupPetPose,
                    foePose: windupFoePose,
                    hold: .milliseconds(1200)
                )
            ]
            if outcome.didHit {
                let lead = outcome.didCrit ? "A critical hit! " : ""
                // The one that got hit recoils.
                let reactionPetPose: WorklingSpriteFrame = defenderSide == .pet ? .hurt : windupPetPose
                let reactionFoePose: FoePose = defenderSide == .foe ? .hurt : windupFoePose
                result.append(
                    Beat(
                        side: attackerSide,
                        text: "\(lead)\(attacker) hits the \(defender) for \(outcome.damage) damage.",
                        petPose: reactionPetPose,
                        foePose: reactionFoePose,
                        isCrit: outcome.didCrit,
                        hpChange: (defenderSide, -outcome.damage),
                        hold: .milliseconds(1500)
                    )
                )
            } else {
                result.append(
                    Beat(
                        side: defenderSide,
                        text: "\(defender) dodges the blow!",
                        hold: .milliseconds(1200)
                    )
                )
            }
            return result

        case let .signature(attacker, defender, outcome):
            return [
                Beat(side: .pet, text: "\(attacker) unleashes its Signature!", petPose: .signature, hold: .milliseconds(1300)),
                Beat(
                    side: .pet,
                    text: "It tears into the \(defender) for \(outcome.damage) damage!",
                    petPose: .signature,
                    foePose: .hurt,
                    hpChange: (.foe, -outcome.damage),
                    hold: .milliseconds(1500)
                )
            ]

        case let .braced(who, regen):
            return [
                Beat(
                    side: .pet,
                    text: "\(who) braces and steadies itself (+\(regen)).",
                    petPose: .brace,
                    hpChange: (.pet, regen),
                    hold: .milliseconds(1200)
                )
            ]

        case let .defeated(who):
            if who == petName {
                return [Beat(side: .foe, text: "\(petName) is downed!", petPose: .downed, foePose: .attack, hold: .milliseconds(1800))]
            }
            return [Beat(side: .pet, text: "The \(who) is defeated!", petPose: .victory, foePose: .hurt, hold: .milliseconds(1800))]

        case .roundBegan, .decisionPoint, .encounterEnded:
            return []
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
            ArenaCombatant(model: model, side: .pet, tint: .green, creatureSize: Self.creatureSize)
            Spacer()
            Text("vs")
                .font(.system(.title3, design: .rounded).weight(.black))
                .foregroundStyle(.white.opacity(0.3))
                .padding(.bottom, 60)
            Spacer()
            ArenaCombatant(model: model, side: .foe, tint: .orange, creatureSize: Self.creatureSize)
        }
        .padding(.horizontal, 34)
        .frame(maxHeight: .infinity)
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

/// One fighter's column: speech bubble, sprite (with ground shadow), name, and
/// HP. Owns the transient impact "juice" — a shake, a white flash, and a rising
/// damage number whenever this side takes a hit.
private struct ArenaCombatant: View {
    @ObservedObject var model: CombatViewModel
    let side: CombatSide
    let tint: Color
    let creatureSize: CGFloat

    @State private var flash: Double = 0
    @State private var floaters: [DamageFloater] = []

    private var name: String { side == .pet ? model.petName : model.foeName }
    private var hp: Int { side == .pet ? model.petHP : model.foeHP }
    private var maxHP: Int { side == .pet ? model.petMaxHP : model.foeMaxHP }
    private var hitToken: Int { side == .pet ? model.petHitToken : model.foeHitToken }
    private var hitAmount: Int { side == .pet ? model.petHitAmount : model.foeHitAmount }
    private var hitCrit: Bool { side == .pet ? model.petHitCrit : model.foeHitCrit }

    var body: some View {
        VStack(spacing: 8) {
            // Bottom-aligned so the bubble grows upward and never shifts the row.
            SpeechBubble(text: model.speaker == side ? model.speechLine : nil)
                .frame(height: 78, alignment: .bottom)

            ZStack(alignment: .bottom) {
                Ellipse()
                    .fill(.black.opacity(0.25))
                    .frame(width: creatureSize * 0.6, height: 14)
                    .blur(radius: 3)
                creature
                    .brightness(flash)
                    .impactShake(trigger: hitToken)
            }
            .frame(height: creatureSize)
            .overlay(alignment: .top) {
                ForEach(floaters) { floater in
                    DamageFloaterView(amount: floater.amount, crit: floater.crit) {
                        floaters.removeAll { $0.id == floater.id }
                    }
                }
            }

            VStack(spacing: 4) {
                Text(name).font(.headline)
                HPBar(fraction: maxHP > 0 ? Double(max(hp, 0)) / Double(maxHP) : 0, tint: tint)
                    .frame(width: 132)
                Text("\(max(hp, 0)) / \(maxHP)")
                    .font(.caption2).monospacedDigit()
                    .foregroundStyle(.white.opacity(0.65))
            }
        }
        .onChange(of: hitToken) { _, token in
            guard token > 0 else { return }
            flash = 0.55
            withAnimation(.easeOut(duration: 0.35)) { flash = 0 }
            floaters.append(DamageFloater(amount: hitAmount, crit: hitCrit))
        }
    }

    @ViewBuilder
    private var creature: some View {
        if side == .pet {
            PetCombatSprite(family: model.petFamily, pose: model.petPose, size: creatureSize)
        } else {
            FoeSprite(foeName: model.foeName, pose: model.foePose, size: creatureSize)
        }
    }
}

/// A damage number that rises and fades above the creature that was hit.
private struct DamageFloater: Identifiable {
    let id = UUID()
    let amount: Int
    let crit: Bool
}

private struct DamageFloaterView: View {
    let amount: Int
    let crit: Bool
    let onDone: () -> Void

    @State private var rise: CGFloat = 6
    @State private var fade: Double = 0

    var body: some View {
        Text(crit ? "\(amount)!" : "\(amount)")
            .font(.system(crit ? .title2 : .title3, design: .rounded).weight(.heavy))
            .foregroundStyle(crit ? .yellow : .white)
            .shadow(color: .black.opacity(0.6), radius: 2, y: 1)
            .offset(y: rise)
            .opacity(fade)
            .onAppear {
                fade = 1
                withAnimation(.easeOut(duration: 0.85)) {
                    rise = -46
                    fade = 0
                }
                Task {
                    try? await Task.sleep(for: .milliseconds(880))
                    onDone()
                }
            }
    }
}

/// The scale-and-shake punch that fires each time `trigger` changes (a hit).
private struct ImpactValue: Sendable {
    var shakeX: CGFloat = 0
    var scale: CGFloat = 1
}

private struct ImpactShake: ViewModifier {
    let trigger: Int

    func body(content: Content) -> some View {
        content.keyframeAnimator(initialValue: ImpactValue(), trigger: trigger) { view, value in
            view.offset(x: value.shakeX).scaleEffect(value.scale)
        } keyframes: { _ in
            KeyframeTrack(\.shakeX) {
                CubicKeyframe(-7, duration: 0.05)
                CubicKeyframe(7, duration: 0.06)
                CubicKeyframe(-4, duration: 0.06)
                CubicKeyframe(0, duration: 0.08)
            }
            KeyframeTrack(\.scale) {
                CubicKeyframe(0.9, duration: 0.06)
                SpringKeyframe(1.0, duration: 0.26)
            }
        }
    }
}

private extension View {
    func impactShake(trigger: Int) -> some View {
        modifier(ImpactShake(trigger: trigger))
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

/// The foe in the arena: renders its idle / attack / hurt sprite (facing the pet
/// natively), bobbing while idle and lunging left into its attack. Falls back to
/// the placeholder for foes whose sprites haven't been drawn yet.
private struct FoeSprite: View {
    let foeName: String
    let pose: FoePose
    let size: CGFloat

    var body: some View {
        if FoeSpriteAsset.hasArt(foeName) {
            TimelineView(.periodic(from: .now, by: 0.9)) { context in
                sprite(at: context.date)
            }
        } else {
            FoePlaceholder(size: size)
        }
    }

    private func sprite(at date: Date) -> some View {
        Group {
            if let image = FoeSpriteAsset.image(foe: foeName, pose: pose) {
                Image(decorative: image, scale: 1, orientation: .up)
                    .resizable()
                    .interpolation(.none)
                    .scaledToFit()
            } else {
                Color.clear
            }
        }
        .frame(width: size, height: size)
        .offset(x: pose == .attack ? -22 : 0, y: idleBob(at: date))
        .animation(.easeOut(duration: 0.14), value: pose)
    }

    private func idleBob(at date: Date) -> CGFloat {
        guard pose == .idle else { return 0 }
        let phase = Int(date.timeIntervalSinceReferenceDate / 0.9)
        return phase.isMultiple(of: 2) ? 0 : -4
    }
}

/// Loads and caches the small foe sprite sets. Each supported foe has an idle,
/// attack, and hurt PNG, loaded once like the family sheets.
private enum FoeSpriteAsset {
    static let moteIdle = load("mote-idle")
    static let moteAttack = load("mote-attack")
    static let moteHurt = load("mote-hurt")

    static func hasArt(_ foe: String) -> Bool {
        resourceBase(for: foe) != nil
    }

    static func image(foe: String, pose: FoePose) -> CGImage? {
        switch (resourceBase(for: foe), pose) {
        case ("mote", .idle): return moteIdle
        case ("mote", .attack): return moteAttack
        case ("mote", .hurt): return moteHurt
        default: return nil
        }
    }

    private static func resourceBase(for foe: String) -> String? {
        switch foe {
        case "Mote": return "mote"
        default: return nil
        }
    }

    private static func load(_ resourceName: String) -> CGImage? {
        let url = Bundle.main.url(forResource: resourceName, withExtension: "png")
            ?? Bundle.module.url(forResource: resourceName, withExtension: "png")
        guard let url, let image = NSImage(contentsOf: url) else {
            NSLog("Worklings could not load the %@ foe sprite.", resourceName)
            return nil
        }
        var rect = NSRect(origin: .zero, size: image.size)
        return image.cgImage(forProposedRect: &rect, context: nil, hints: nil)
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
                        .font(.system(.subheadline, design: .rounded).weight(.semibold))
                        .foregroundStyle(.black)
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: 190)
                        .padding(.horizontal, 13)
                        .padding(.vertical, 7)
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
