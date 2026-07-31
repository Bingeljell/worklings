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
    /// The side that just fell — the arena plays a smoke poof over it, at the
    /// moment of defeat, before the end screen appears.
    @Published private(set) var defeatedSide: CombatSide?
    /// A big centre countdown before each action (3 → 2 → 1); nil hides it.
    @Published private(set) var countdownValue: Int?
    /// The creature the current speech bubble sits above, and its short line —
    /// nil hides the bubbles.
    @Published private(set) var speaker: CombatSide?
    @Published private(set) var speechLine: String?
    /// Scene-setting narration (e.g. the encounter's opening line). Shown as a
    /// centered banner across the stage, distinct from the combatants' action
    /// bubbles; nil hides it.
    @Published private(set) var narrativeLine: String?

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
        /// Scene-setting narration rather than a combatant's line — shown in the
        /// centered banner instead of a speech bubble.
        var isNarrative = false
        var petPose: WorklingSpriteFrame = .idle
        var foePose: FoePose = .idle
        var isCrit = false
        var hpChange: (side: CombatSide, amount: Int)?
        var defeats: CombatSide?
        var countdown: Int?
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
        narrativeLine = nil
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
        countdownValue = beat.countdown
        // A countdown tick is just the big number: no bubble, creatures idle.
        if beat.countdown != nil {
            speaker = nil
            speechLine = nil
            narrativeLine = nil
            petPose = restingPose
            foePose = .idle
            return
        }

        // Scene-setting narration goes to the centered banner, not a bubble.
        if beat.isNarrative {
            narrativeLine = beat.text
            speaker = nil
            speechLine = nil
        } else {
            narrativeLine = nil
            speaker = beat.side
            speechLine = beat.text
        }
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
        if let defeated = beat.defeats { defeatedSide = defeated }
        if let text = beat.text { lines.append(text) }
    }

    private func showDecision(_ reason: DecisionReason) {
        awaitingDecision = reason
        narrativeLine = nil
        speaker = .pet
        speechLine = reason == .lowHP ? "I'm hurting — what now?" : "What's the plan?"
    }

    /// Expands one engine event into the readable beats it plays. A strike becomes
    /// a wind-up ("… attacks the …") then a result ("… hits … for N damage"), each
    /// held long enough to read; the foe's own wind-up is its "gearing up" beat.
    private func beats(for event: CombatEvent) -> [Beat] {
        switch event {
        case let .encounterBegan(_, foe):
            return [Beat(text: "A \(foe) blocks the way…", isNarrative: true, hold: .milliseconds(2200))]

        case let .struck(attacker, defender, outcome):
            let attackerSide: CombatSide = attacker == petName ? .pet : .foe
            let defenderSide: CombatSide = defender == petName ? .pet : .foe
            let petAttacking = attackerSide == .pet
            // Intro line, then the 3-2-1 countdown, then the swing + hit together.
            var out: [Beat] = [
                Beat(side: attackerSide, text: "\(attacker) sets upon the \(defender).", hold: .milliseconds(900))
            ]
            out += countdownBeats()
            if outcome.didHit {
                let lead = outcome.didCrit ? "A critical hit! " : ""
                out.append(
                    Beat(
                        side: attackerSide,
                        text: "\(lead)\(attacker) hits the \(defender) for \(outcome.damage) damage!",
                        petPose: petAttacking ? .strike : .hurt,
                        foePose: petAttacking ? .hurt : .attack,
                        isCrit: outcome.didCrit,
                        hpChange: (defenderSide, -outcome.damage),
                        hold: .milliseconds(1500)
                    )
                )
            } else {
                out.append(
                    Beat(
                        side: defenderSide,
                        text: "\(attacker) swings — the \(defender) dodges!",
                        petPose: petAttacking ? .strike : .idle,
                        foePose: petAttacking ? .idle : .attack,
                        hold: .milliseconds(1300)
                    )
                )
            }
            return out

        case let .signature(attacker, defender, outcome):
            var out: [Beat] = [
                Beat(side: .pet, text: "\(attacker) gathers power for its Signature…", hold: .milliseconds(900))
            ]
            out += countdownBeats()
            out.append(
                Beat(
                    side: .pet,
                    text: "\(attacker) unleashes — the \(defender) takes \(outcome.damage)!",
                    petPose: .signature,
                    foePose: .hurt,
                    hpChange: (.foe, -outcome.damage),
                    hold: .milliseconds(1600)
                )
            )
            return out

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
                return [Beat(side: .foe, text: "\(petName) is downed!", petPose: .downed, foePose: .attack, defeats: .pet, hold: .milliseconds(1700))]
            }
            return [Beat(side: .pet, text: "The \(who) is defeated!", petPose: .victory, foePose: .hurt, defeats: .foe, hold: .milliseconds(1700))]

        case .roundBegan, .decisionPoint, .encounterEnded:
            return []
        }
    }

    private var restingPose: WorklingSpriteFrame {
        let fraction = petMaxHP > 0 ? Double(petHP) / Double(petMaxHP) : 1
        return fraction < session.combatRates.lowHPEventThreshold ? .lowHP : .idle
    }

    /// The 3 → 2 → 1 beats shown before an action, for anticipation.
    private func countdownBeats() -> [Beat] {
        [3, 2, 1].map { Beat(countdown: $0, hold: .milliseconds(700)) }
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
        .overlay {
            if let outcome = model.outcome {
                EndScreen(outcome: outcome, model: model, onReturn: onClose)
            }
        }
    }

    private var stageBackground: some View {
        ArenaBackground()
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
        .overlay(alignment: .top) { NarrativeBanner(text: model.narrativeLine) }
        .overlay { countdownOverlay }
    }

    @ViewBuilder
    private var countdownOverlay: some View {
        ZStack {
            if let value = model.countdownValue {
                Text("\(value)")
                    .font(.system(size: 96, weight: .black, design: .rounded))
                    .foregroundStyle(.white)
                    .shadow(color: .black.opacity(0.6), radius: 10, y: 3)
                    .transition(.scale(scale: 1.6).combined(with: .opacity))
                    .id(value)
            }
        }
        .animation(.spring(response: 0.3, dampingFraction: 0.6), value: model.countdownValue)
        .allowsHitTesting(false)
    }

    private var controlBar: some View {
        Group {
            if model.outcome != nil {
                Color.clear.frame(height: 1) // the end screen takes over
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

    private func name(for approach: Approach) -> String {
        switch approach {
        case .aggressive: "Aggressive"
        case .careful: "Careful"
        case .clever: "Clever"
        }
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

/// The end-of-fight screen: the winner takes centre stage in its victory pose
/// (with a pop-in and sparkles), the loser vanishes in a puff of smoke, then the
/// result and Return button fade in.
private struct EndScreen: View {
    let outcome: EncounterResolution
    @ObservedObject var model: CombatViewModel
    let onReturn: () -> Void

    @State private var titleShown = false
    @State private var winnerScale: CGFloat = 0.4
    @State private var winnerBob = false
    @State private var footerShown = false

    private var won: Bool { outcome.tier != .downed }

    var body: some View {
        ZStack {
            // Opaque so the busy arena (HP bars, the other fighter) is fully
            // hidden — just the cave and the victor.
            ArenaBackground()
            Color.black.opacity(0.5)

            VStack(spacing: 18) {
                Text(won ? "Victory!" : "Defeated…")
                    .font(.system(size: 46, weight: .black, design: .rounded))
                    .foregroundStyle(won ? Color.green : Color.orange)
                    .shadow(color: .black.opacity(0.5), radius: 6, y: 3)
                    .scaleEffect(titleShown ? 1 : 0.5)
                    .opacity(titleShown ? 1 : 0)

                winner
                    .scaleEffect(winnerScale)
                    .offset(y: winnerBob ? -6 : 0)
                    .overlay { if won { Sparkles() } }
                    .frame(height: 180)

                if footerShown {
                    VStack(spacing: 10) {
                        Text("\(tierName(outcome.tier)) — \(exitBlurb(outcome.tier))")
                            .font(.headline)
                            .foregroundStyle(.white.opacity(0.85))
                        if outcome.xpGained > 0 {
                            Label("+\(Int(outcome.xpGained)) XP", systemImage: "sparkles")
                                .font(.title3.bold())
                                .foregroundStyle(.white)
                        }
                        Button(action: onReturn) {
                            Text("Return").frame(width: 160)
                        }
                        .buttonStyle(.borderedProminent)
                        .controlSize(.large)
                    }
                    .transition(.opacity.combined(with: .move(edge: .bottom)))
                }
            }
            .padding(28)
        }
        .onAppear(perform: runSequence)
    }

    @ViewBuilder
    private var winner: some View {
        if won {
            PetCombatSprite(family: model.petFamily, pose: .victory, size: 150)
        } else {
            FoeSprite(foeName: model.foeName, pose: .attack, size: 150)
        }
    }

    private func runSequence() {
        withAnimation(.spring(response: 0.5, dampingFraction: 0.6)) {
            winnerScale = 1
            titleShown = true
        }
        withAnimation(.easeInOut(duration: 0.9).repeatForever(autoreverses: true)) {
            winnerBob = true
        }
        Task {
            try? await Task.sleep(for: .milliseconds(450))
            withAnimation(.easeOut(duration: 0.35)) { footerShown = true }
        }
    }
}

/// A ring of twinkling sparkles for the victor.
private struct Sparkles: View {
    @State private var lit = false

    private let spots: [CGPoint] = [
        CGPoint(x: -62, y: -46), CGPoint(x: 66, y: -34), CGPoint(x: -50, y: 46),
        CGPoint(x: 58, y: 52), CGPoint(x: 4, y: -74)
    ]

    var body: some View {
        ZStack {
            ForEach(spots.indices, id: \.self) { index in
                Image(systemName: "sparkle")
                    .font(.system(size: 18))
                    .foregroundStyle(.yellow)
                    .offset(x: spots[index].x, y: spots[index].y)
                    .opacity(lit ? 1 : 0.2)
                    .scaleEffect(lit ? 1 : 0.5)
                    .animation(
                        .easeInOut(duration: 0.7).repeatForever(autoreverses: true)
                            .delay(Double(index) * 0.12),
                        value: lit
                    )
            }
        }
        .onAppear { lit = true }
    }
}

/// The dungeon stage: the painted cave backdrop with a slowly-drifting
/// translucent atmosphere layer over it for parallax life, and a faint darkening
/// so the fighters and text read on top. Falls back to a gradient if the art is
/// missing.
private struct ArenaBackground: View {
    var body: some View {
        ZStack {
            LinearGradient(
                colors: [Color(red: 0.11, green: 0.10, blue: 0.17), Color(red: 0.05, green: 0.05, blue: 0.09)],
                startPoint: .top, endPoint: .bottom
            )
            if let backdrop = DungeonArtAsset.caveBackdrop {
                Image(decorative: backdrop, scale: 1, orientation: .up)
                    .resizable()
                    .scaledToFill()
            }
            if let atmosphere = DungeonArtAsset.atmosphere {
                AtmosphereDrift(image: atmosphere)
                    .allowsHitTesting(false)
            }
            LinearGradient(
                colors: [.black.opacity(0.42), .black.opacity(0.14), .black.opacity(0.34)],
                startPoint: .top, endPoint: .bottom
            )
        }
        .clipped()
    }
}

/// Scrolls the seamless, horizontally-tileable atmosphere overlay to give the
/// air a slow drift. Two copies chase each other so it loops without a seam.
private struct AtmosphereDrift: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    let image: CGImage

    private static let pointsPerSecond = 10.0

    var body: some View {
        GeometryReader { geo in
            let tileWidth = max(geo.size.height * CGFloat(image.width) / CGFloat(image.height), 1)
            if reduceMotion {
                tile(tileWidth, geo.size.height)
            } else {
                TimelineView(.animation) { context in
                    let elapsed = context.date.timeIntervalSinceReferenceDate
                    let phase = CGFloat((elapsed * Self.pointsPerSecond)
                        .truncatingRemainder(dividingBy: Double(tileWidth)))
                    HStack(spacing: 0) {
                        tile(tileWidth, geo.size.height)
                        tile(tileWidth, geo.size.height)
                    }
                    .offset(x: -phase)
                    .frame(width: geo.size.width, height: geo.size.height, alignment: .leading)
                    .clipped()
                }
            }
        }
    }

    private func tile(_ width: CGFloat, _ height: CGFloat) -> some View {
        // The overlay is a pale haze; keep it very faint so it drifts as
        // atmosphere without washing the cave out or flattening the fighters.
        Image(decorative: image, scale: 1, orientation: .up)
            .resizable()
            .frame(width: width, height: height)
            .opacity(0.14)
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
    @State private var poofFrame: Int?
    @State private var poofed = false

    private var name: String { side == .pet ? model.petName : model.foeName }
    private var hp: Int { side == .pet ? model.petHP : model.foeHP }
    private var maxHP: Int { side == .pet ? model.petMaxHP : model.foeMaxHP }
    private var hitToken: Int { side == .pet ? model.petHitToken : model.foeHitToken }
    private var hitAmount: Int { side == .pet ? model.petHitAmount : model.foeHitAmount }
    private var hitCrit: Bool { side == .pet ? model.petHitCrit : model.foeHitCrit }

    var body: some View {
        VStack(spacing: 8) {
            // Bottom-aligned so the bubble grows upward (higher on the stage,
            // occupying more of the space) and never shifts the row.
            SpeechBubble(text: model.speaker == side ? model.speechLine : nil)
                .frame(height: 112, alignment: .bottom)

            ZStack(alignment: .bottom) {
                Ellipse()
                    .fill(.black.opacity(0.25))
                    .frame(width: creatureSize * 0.6, height: 14)
                    .blur(radius: 3)
                    .opacity(poofed ? 0 : 1)
                if !poofed {
                    creature
                        .brightness(flash)
                        .impactShake(trigger: hitToken)
                        .opacity((poofFrame ?? 0) < 4 ? 1 : 0)
                }
                if let frame = poofFrame {
                    SmokeEffectSprite(frameIndex: frame, size: creatureSize)
                }
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
        .onChange(of: model.defeatedSide) { _, defeated in
            guard defeated == side, poofFrame == nil, !poofed else { return }
            Task {
                for frame in 0..<8 {
                    poofFrame = frame
                    try? await Task.sleep(for: .milliseconds(65))
                }
                poofed = true
                poofFrame = nil
            }
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
        bundledCGImage(resourceName)
    }
}

/// The dungeon's background art, loaded once.
private enum DungeonArtAsset {
    static let caveBackdrop = bundledCGImage("cache-warren-cave-backdrop")
    static let atmosphere = bundledCGImage("cache-warren-atmosphere-overlay")
}

/// Loads a bundled PNG as a `CGImage`, from the app bundle or the SwiftPM module
/// bundle. Shared by the foe and dungeon art loaders.
private func bundledCGImage(_ resourceName: String) -> CGImage? {
    let url = Bundle.main.url(forResource: resourceName, withExtension: "png")
        ?? Bundle.module.url(forResource: resourceName, withExtension: "png")
    guard let url, let image = NSImage(contentsOf: url) else {
        NSLog("Worklings could not load image %@.", resourceName)
        return nil
    }
    var rect = NSRect(origin: .zero, size: image.size)
    return image.cgImage(forProposedRect: &rect, context: nil, hints: nil)
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

/// Scene-setting narration across the top of the stage — a centered, italic
/// banner that reads as the dungeon's own voice, distinct from the combatants'
/// speech bubbles. Empty when `text` is nil.
private struct NarrativeBanner: View {
    let text: String?

    var body: some View {
        ZStack {
            if let text {
                Text(text)
                    .font(.system(.title3, design: .serif).italic())
                    .foregroundStyle(.white)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: 420)
                    .padding(.horizontal, 22)
                    .padding(.vertical, 12)
                    .background(.black.opacity(0.55), in: Capsule())
                    .overlay(Capsule().stroke(.white.opacity(0.18), lineWidth: 1))
                    .shadow(color: .black.opacity(0.4), radius: 8, y: 3)
                    .transition(.move(edge: .top).combined(with: .opacity))
                    .id(text)
            }
        }
        .padding(.top, 8)
        .animation(.spring(response: 0.35, dampingFraction: 0.8), value: text)
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
                        .font(.system(.title3, design: .rounded).weight(.bold))
                        .foregroundStyle(.black)
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: 250)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 10)
                        .background(.white, in: RoundedRectangle(cornerRadius: 16))
                        .shadow(color: .black.opacity(0.35), radius: 6, y: 2)
                    BubbleTail()
                        .fill(.white)
                        .frame(width: 18, height: 10)
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
