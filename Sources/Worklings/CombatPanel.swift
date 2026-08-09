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
    /// Set the instant this encounter reaches an ending, so the arena hands control
    /// back to the delve (which shows the push prompt or the delve-end screen).
    @Published private(set) var isFinished = false
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
    /// Called once, at the ending, with the result the delve records: whether the
    /// pet won and the HP it walked out with (carried into the next encounter).
    private let onComplete: (_ victory: Bool, _ petHPRemaining: Int) -> Void
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
        /// An optional one-shot sound cue to fire as this beat is applied.
        var sound: CombatSound?
        var hold: Duration
    }

    /// Drives one encounter of a delve. The `encounter` is built by the `Delve`
    /// (carrying HP + the per-encounter seed); this view model only animates it and
    /// reports the ending through `onComplete`. Audio and the BGM bed are managed by
    /// the owning `DelveViewModel`, which spans the whole delve.
    init(
        session: PetSession,
        foe: Foe,
        encounter: CombatEncounter,
        onComplete: @escaping (_ victory: Bool, _ petHPRemaining: Int) -> Void
    ) {
        self.session = session
        self.foe = foe
        self.encounter = encounter
        self.onComplete = onComplete
        petName = encounter.pet.name
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
        // A countdown tick keeps the attacker's announcement bubble up (so the
        // "X attacks Y" line stays readable while 3-2-1 builds) and just idles the
        // creatures under the big number.
        if beat.countdown != nil {
            CombatAudio.shared.play(.tick, volume: 0.5)
            petPose = restingPose
            foePose = .idle
            return
        }

        if let sound = beat.sound {
            CombatAudio.shared.play(sound)
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
        switch reason {
        case .lowHP: speechLine = "I'm hurting — what now?"
        case .opening: speechLine = "It's wide open — now's the moment!"
        case .telegraph: speechLine = "It's winding up — Brace, or I take it?"
        case .cadence: speechLine = "What's the plan?"
        }
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
            // One announcement — "X attacks Y!" — that stays up through the
            // countdown AND the swing, so the action reads while it plays out. The
            // rising damage number carries the amount; a miss adds a short dodge.
            let announce = "\(subject(attackerSide)) attacks \(object(defenderSide))!"
            var out: [Beat] = [
                Beat(side: attackerSide, text: announce, hold: .milliseconds(800))
            ]
            out += countdownBeats()
            if outcome.didHit {
                out.append(
                    Beat(
                        side: attackerSide,
                        text: announce,
                        petPose: petAttacking ? .strike : .hurt,
                        foePose: petAttacking ? .hurt : .attack,
                        isCrit: outcome.didCrit,
                        hpChange: (defenderSide, -outcome.damage),
                        sound: outcome.didCrit ? .crit : .hit,
                        hold: .milliseconds(1800)
                    )
                )
            } else {
                out.append(
                    Beat(
                        side: attackerSide,
                        text: announce,
                        petPose: petAttacking ? .strike : .idle,
                        foePose: petAttacking ? .idle : .attack,
                        sound: .dodge,
                        hold: .milliseconds(1000)
                    )
                )
                out.append(
                    Beat(
                        side: defenderSide,
                        text: "\(subject(defenderSide)) dodges!",
                        hold: .milliseconds(1400)
                    )
                )
            }
            return out

        case let .signature(_, _, outcome):
            let announce = "\(subject(.pet)) unleashes its Signature!"
            var out: [Beat] = [
                Beat(side: .pet, text: announce, hold: .milliseconds(800))
            ]
            out += countdownBeats()
            out.append(
                Beat(
                    side: .pet,
                    text: announce,
                    petPose: .signature,
                    foePose: .hurt,
                    hpChange: (.foe, -outcome.damage),
                    sound: .unleash,
                    hold: .milliseconds(1900)
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
                    sound: .brace,
                    hold: .milliseconds(1700)
                )
            ]

        case let .grabbed(attacker, target, agilityLoss):
            return [
                Beat(
                    side: .foe,
                    text: "\(attacker) grabs \(target)! Its agility sags (−\(agilityLoss)).",
                    petPose: .hurt,
                    foePose: .attack,
                    sound: .snare,
                    hold: .milliseconds(2000)
                )
            ]

        case let .phased(who):
            return [
                Beat(
                    side: .foe,
                    text: "The \(who) blurs aside — your next blow will slip!",
                    foePose: .idle,
                    sound: .phase,
                    hold: .milliseconds(1800)
                )
            ]

        case let .telegraphed(who):
            return [
                Beat(
                    side: .foe,
                    text: "The \(who) heaves back — a crushing blow is coming!",
                    foePose: .attack,
                    sound: .telegraph,
                    hold: .milliseconds(2000)
                )
            ]

        case let .slammed(attacker, defender, outcome):
            return [
                Beat(
                    side: .foe,
                    text: "SLAM! \(attacker) crushes \(defender) for \(outcome.damage)!",
                    petPose: .hurt,
                    foePose: .attack,
                    isCrit: outcome.didCrit,
                    hpChange: (.pet, -outcome.damage),
                    sound: .slam,
                    hold: .milliseconds(2200)
                )
            ]

        case let .hardened(who, guardGain):
            return [
                Beat(
                    side: .foe,
                    text: "The \(who) hardens — its guard rises! (+\(guardGain))",
                    foePose: .idle,
                    sound: .harden,
                    hold: .milliseconds(1800)
                )
            ]

        case let .defeated(who):
            if who == petName {
                return [Beat(side: .foe, text: "\(petName) is downed!", petPose: .downed, foePose: .attack, defeats: .pet, hold: .milliseconds(2300))]
            }
            return [Beat(side: .pet, text: "The \(who) is defeated!", petPose: .victory, foePose: .hurt, defeats: .foe, sound: .poof, hold: .milliseconds(2300))]

        case .roundBegan, .decisionPoint, .encounterEnded:
            return []
        }
    }

    /// Sentence-start reference to a combatant: the pet by its name, a foe as
    /// "The Foe". Keeps the narration grammatical either way round.
    private func subject(_ side: CombatSide) -> String {
        side == .pet ? petName : "The \(foeName)"
    }

    /// Mid-sentence reference: the pet by its name, a foe as "the Foe".
    private func object(_ side: CombatSide) -> String {
        side == .pet ? petName : "the \(foeName)"
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
        isFinished = true
        onComplete(encounter.status == .petVictory, encounter.pet.currentHP)
    }
}

// MARK: - Delve coordinator

/// Spans a whole delve: the briefing, each encounter in turn (carrying HP), the
/// bank-vs-push beat between them, and the single write-back at the end. It owns
/// the current encounter's `CombatViewModel` and the BGM bed, and drives the
/// `Delve` engine from `CompanionCore`.
@MainActor
final class DelveViewModel: ObservableObject {
    enum Phase: Equatable {
        case briefing      // the opening narration
        case fighting      // an encounter is playing
        case pushChoice    // cleared a regular foe — bank or push
        case ended         // the delve is over; the end screen shows
    }

    @Published private(set) var phase: Phase = .briefing
    @Published private(set) var current: CombatViewModel?
    @Published private(set) var resolution: DelveResolution?
    /// The Approach the pet starts each encounter on, chosen in the briefing.
    @Published var startingApproach: Approach = .aggressive

    let session: PetSession
    private var delve: Delve
    private let seed: UInt64
    /// The foe of the most recent encounter, for the push prompt and the end
    /// screen's loser sprite.
    private(set) var lastFoeName: String

    init(session: PetSession, seed: UInt64) {
        self.session = session
        self.seed = seed
        self.delve = Self.makeDelve(session: session, seed: seed)
        self.lastFoeName = delve.currentFoe?.name ?? ""
    }

    private static func makeDelve(session: PetSession, seed: UInt64) -> Delve {
        Delve.cacheWarren(
            pet: session.makePetCombatant(),
            effectiveness: session.combatEffectiveness,
            rates: session.combatRates,
            baseSeed: seed
        )
    }

    // Briefing display
    var previewFoeNames: [String] { delve.foes.map(\.name) }
    var bossName: String { delve.boss.name }

    // In-flight display
    var progressText: String { "Encounter \(delve.encounterNumber) of \(delve.totalEncounters)" }
    var carriedHP: Int { delve.carriedHP }
    var nextIsBoss: Bool { delve.index + 1 == delve.foes.count }
    var nextFoeName: String? {
        let next = delve.index + 1
        return next < delve.allFoes.count ? delve.allFoes[next].name : nil
    }

    func descend() {
        // Rebuilt here, not at init: the briefing is where gear gets swapped, so
        // the combatant has to be read from the pet as it stands at the moment it
        // actually walks in. The delve built at init only ever backed the foe
        // preview.
        delve = Self.makeDelve(session: session, seed: seed)
        delve.descend()
        CombatAudio.shared.play(.enter)
        CombatAudio.shared.startBGM(boss: false)
        startEncounter()
    }

    private func startEncounter() {
        guard let foe = delve.currentFoe,
              let encounter = delve.makeEncounter(approach: startingApproach) else { return }
        lastFoeName = foe.name
        if delve.isBossEncounter { CombatAudio.shared.startBGM(boss: true) }
        current = CombatViewModel(session: session, foe: foe, encounter: encounter) { [weak self] victory, hp in
            self?.encounterEnded(victory: victory, hpRemaining: hp)
        }
        phase = .fighting
    }

    private func encounterEnded(victory: Bool, hpRemaining: Int) {
        delve.recordOutcome(petVictory: victory, petHPRemaining: hpRemaining)
        switch delve.status {
        case .awaitingPushChoice: phase = .pushChoice
        case .completed, .retreated: finishDelve()
        case .briefing, .inEncounter: break
        }
    }

    func bank() {
        delve.bank()
        finishDelve()
    }

    func pushDeeper() {
        delve.pushDeeper()
        startEncounter()
    }

    private func finishDelve() {
        guard let res = delve.resolution(applyingTo: session.state) else { return }
        session.applyDelveResolution(res)
        resolution = res
        CombatAudio.shared.stopBGM()
        CombatAudio.shared.play(res.tier == .downed ? .defeat : .victory, volume: 0.9)
        phase = .ended
    }
}

// MARK: - Panel window

/// Hosts the delve panel in its own floating window for the length of a delve,
/// then tears it down. One delve at a time.
@MainActor
final class CombatPanelController {
    private var panel: NSPanel?
    private var delve: DelveViewModel?
    private var onDismiss: (() -> Void)?

    var isPresenting: Bool { panel != nil }

    /// Opens the arena for a full delve. `onDismiss` runs when it closes (however it
    /// closes), so the caller can bring the desktop companion back.
    func present(session: PetSession, seed: UInt64, onDismiss: @escaping () -> Void) {
        dismiss()
        self.onDismiss = onDismiss

        let delve = DelveViewModel(session: session, seed: seed)
        self.delve = delve

        let root = DelvePanelView(delve: delve, onClose: { [weak self] in self?.dismiss() })
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
        CombatAudio.shared.stopBGM()
        if wasPresenting { CombatAudio.shared.play(.returnChime) }
        panel?.orderOut(nil)
        panel = nil
        delve = nil
        if wasPresenting {
            let callback = onDismiss
            onDismiss = nil
            callback?()
        }
    }
}

// MARK: - View

/// The root of the delve panel: switches between the briefing, the current
/// encounter's arena, and overlays the bank/push prompt and the end screen.
struct DelvePanelView: View {
    @ObservedObject var delve: DelveViewModel
    var onClose: () -> Void

    var body: some View {
        ZStack {
            switch delve.phase {
            case .briefing:
                BriefingView(delve: delve, onClose: onClose)
            case .fighting, .pushChoice, .ended:
                if let model = delve.current {
                    CombatPanelView(model: model, onClose: onClose, progressText: delve.progressText)
                } else {
                    ArenaBackground()
                }
            }

            if delve.phase == .pushChoice {
                PushChoiceCard(delve: delve)
                    .transition(.opacity)
            }
            if delve.phase == .ended, let res = delve.resolution {
                DelveEndScreen(
                    tier: res.tier,
                    xpGained: Int(res.xpGained),
                    bossDefeated: res.bossDefeated,
                    banked: res.banked,
                    itemDropped: res.itemDropped,
                    petFamily: delve.session.state.family,
                    foeName: delve.lastFoeName,
                    onReturn: onClose
                )
            }
        }
        .frame(width: 600, height: 480)
        .animation(.easeInOut(duration: 0.25), value: delve.phase)
    }
}

/// The opening narration — storytelling that sets the vibe and hints at how to
/// prep — and the prep itself: the loadout and the starting Approach. The
/// narration's one gameplay job is to make these two picks feel informed, so they
/// sit on the same screen as the story that motivates them.
private struct BriefingView: View {
    @ObservedObject var delve: DelveViewModel
    var onClose: () -> Void

    private let narration = """
    The floor gives way to the buried strata of the machine you live in — the \
    Cache Warren. Down here the clutter takes shape and bites back: a scurrying \
    Scamp, a grabbing Snag, a flickering blur that won't hold still… and \
    something heavy waiting at the very bottom. Pack for accuracy, or bring a \
    ward — then descend.
    """

    var body: some View {
        ZStack {
            ArenaBackground()
            Color.black.opacity(0.45)

            VStack(spacing: 13) {
                Text("The Cache Warren")
                    .font(.system(size: 30, weight: .black, design: .rounded))
                    .shadow(color: .black.opacity(0.5), radius: 6, y: 3)

                Text(narration)
                    .font(.callout)
                    .foregroundStyle(.white.opacity(0.85))
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, 24)

                foePreview

                LoadoutBar(session: delve.session)

                VStack(spacing: 5) {
                    Text("Set your opening approach").font(.caption.bold()).foregroundStyle(.white.opacity(0.7))
                    Picker("Approach", selection: $delve.startingApproach) {
                        Text("Aggressive").tag(Approach.aggressive)
                        Text("Careful").tag(Approach.careful)
                        Text("Clever").tag(Approach.clever)
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(maxWidth: 320)
                }

                HStack(spacing: 14) {
                    Button("Not now", action: onClose)
                        .buttonStyle(.bordered)
                    Button(action: { delve.descend() }) {
                        Text("Descend").frame(width: 150)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                }
                .padding(.top, 2)
            }
            .padding(.horizontal, 28)
            .padding(.vertical, 20)
        }
        .foregroundStyle(.white)
    }

    private var foePreview: some View {
        HStack(spacing: 8) {
            ForEach(delve.previewFoeNames, id: \.self) { name in
                Text(name)
                    .font(.caption2.bold())
                    .padding(.horizontal, 10).padding(.vertical, 5)
                    .background(Capsule().fill(.white.opacity(0.12)))
            }
            Text("? ? ?")
                .font(.caption2.bold())
                .padding(.horizontal, 10).padding(.vertical, 5)
                .background(Capsule().fill(.orange.opacity(0.18)))
                .help("Something waits at the bottom of the Warren.")
        }
    }
}

/// The prep beat — the payoff of the briefing's narration. Three slots, each a
/// menu over what the Workling actually owns, plus a running readout of what the
/// picks are worth. The readout is the point: a loadout choice the player can't
/// price is just a menu, so the stat delta is visible *before* the descent rather
/// than inferred from how the fight went.
///
/// This is the *quick* prep, kept on the briefing where the narration motivates
/// it; the Character Screen is gear's actual home. Both price items through
/// `GearPricing`, so the two can only ever say the same thing.
private struct LoadoutBar: View {
    @ObservedObject var session: PetSession

    private var pricing: GearPricing { GearPricing(session: session) }

    var body: some View {
        VStack(spacing: 7) {
            Text("Pack your loadout")
                .font(.caption.bold())
                .foregroundStyle(.white.opacity(0.7))

            HStack(spacing: 8) {
                ForEach(ItemSlot.allCases, id: \.self) { slot in
                    slotMenu(for: slot)
                }
            }

            statLine
        }
    }

    private func slotMenu(for slot: ItemSlot) -> some View {
        let equipped = session.state.loadout[slot]
        let owned = session.state.availableItems(for: slot)

        return Menu {
            ForEach(owned, id: \.self) { item in
                Button(pricing.menuLabel(for: item)) { session.equip(item, in: slot) }
            }
            if !owned.isEmpty {
                Divider()
            }
            Button("Leave empty") { session.equip(nil, in: slot) }
        } label: {
            VStack(spacing: 1) {
                Text(slot.displayName.uppercased())
                    .font(.system(size: 9, weight: .heavy))
                    .foregroundStyle(.white.opacity(0.45))
                Text(equipped?.displayName ?? "Empty")
                    .font(.caption.bold())
                    .foregroundStyle(.white.opacity(equipped == nil ? 0.35 : 1))
                    .lineLimit(1)
            }
            .frame(width: 112)
            .padding(.vertical, 6)
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(.white.opacity(equipped == nil ? 0.07 : 0.14))
            )
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .help(equipped.map { "\($0.flavor)\n\n\(slot.fantasy)" } ?? slot.fantasy)
    }

    /// What the whole loadout is worth, in the same stat vocabulary the sheet
    /// uses.
    private var statLine: some View {
        let parts = pricing.statLineParts(for: session.state.loadout)
        let attunement = pricing.attunementExplanation(for: session.state.loadout)

        return HStack(spacing: 6) {
            if parts.isEmpty {
                Text("Nothing equipped — you'll fight on your own numbers.")
                    .foregroundStyle(.white.opacity(0.4))
            } else {
                Text(parts.joined(separator: " · "))
                    .foregroundStyle(.green.opacity(0.85))
                if let attunement {
                    Text("✦ attuned")
                        .foregroundStyle(.yellow.opacity(0.85))
                        .help(attunement)
                }
            }
        }
        .font(.caption2.bold())
    }
}

/// The press-your-luck beat between encounters: bank the run with what you've
/// earned, or push deeper toward the boss at rising attrition.
private struct PushChoiceCard: View {
    @ObservedObject var delve: DelveViewModel

    var body: some View {
        ZStack {
            Color.black.opacity(0.55)

            VStack(spacing: 14) {
                Text("The \(delve.lastFoeName) falls!")
                    .font(.title2.bold())
                    .foregroundStyle(.green)

                Text("You're at \(delve.carriedHP) HP. Bank the delve, or press deeper?")
                    .font(.callout)
                    .foregroundStyle(.white.opacity(0.85))
                    .multilineTextAlignment(.center)

                Group {
                    if delve.nextIsBoss {
                        Text("Something heavy stirs below…")
                    } else if let next = delve.nextFoeName {
                        Text("Ahead: the \(next).")
                    }
                }
                .font(.caption)
                .foregroundStyle(.white.opacity(0.6))

                HStack(spacing: 14) {
                    Button(action: { delve.bank() }) {
                        Text("Bank & leave").frame(width: 140)
                    }
                    .buttonStyle(.bordered)
                    Button(action: { delve.pushDeeper() }) {
                        Text(delve.nextIsBoss ? "Face the boss" : "Push deeper").frame(width: 140)
                    }
                    .buttonStyle(.borderedProminent)
                }
                .controlSize(.large)
            }
            .padding(28)
            .background(RoundedRectangle(cornerRadius: 18).fill(.black.opacity(0.5)))
            .padding(40)
        }
        .foregroundStyle(.white)
    }
}

struct CombatPanelView: View {
    @ObservedObject var model: CombatViewModel
    var onClose: () -> Void
    /// Where in the delve this fight sits, e.g. "Encounter 2 of 4" — shown in the
    /// header so the chain reads as a journey.
    var progressText: String? = nil

    @State private var approach: Approach = .aggressive
    @State private var unleash = false

    private static let creatureSize: CGFloat = 150
    /// Each combatant column is fixed-width so the sprite never shifts when its
    /// speech bubble grows, shrinks, or disappears (which the countdown does
    /// constantly). Without this the flanking Spacers shove the sprites sideways.
    private static let columnWidth: CGFloat = 244

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
        ArenaBackground()
    }

    private var header: some View {
        HStack {
            Text("The Cache Warren")
                .font(.title3.bold())
            if let progressText {
                Text(progressText)
                    .font(.caption.bold())
                    .foregroundStyle(.white.opacity(0.6))
                    .padding(.leading, 6)
            }
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
                .frame(width: Self.columnWidth)
            Spacer()
            Text("vs")
                .font(.system(.title3, design: .rounded).weight(.black))
                .foregroundStyle(.white.opacity(0.3))
                .padding(.bottom, 60)
            Spacer()
            ArenaCombatant(model: model, side: .foe, tint: .orange, creatureSize: Self.creatureSize)
                .frame(width: Self.columnWidth)
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
            if model.isFinished {
                Color.clear.frame(height: 1) // the delve takes over (push prompt or end screen)
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

/// The end-of-delve screen: the winner takes centre stage in its victory pose
/// (with a pop-in and sparkles), the loser vanishes in a puff of smoke, then the
/// result and Return button fade in. Driven by the finished `DelveResolution`.
private struct DelveEndScreen: View {
    let tier: ExitTier
    let xpGained: Int
    let bossDefeated: Bool
    let banked: Bool
    let itemDropped: Item?
    let petFamily: PetFamily
    let foeName: String
    let onReturn: () -> Void

    @State private var titleShown = false
    @State private var winnerScale: CGFloat = 0.4
    @State private var winnerBob = false
    @State private var footerShown = false

    private var won: Bool { tier != .downed }

    /// The headline reflects *how* the delve ended, not just win/lose.
    private var title: String {
        if bossDefeated { return "Warren Cleared!" }
        if banked { return "Delve Banked" }
        return "Driven Back…"
    }

    var body: some View {
        ZStack {
            // Opaque so the busy arena (HP bars, the other fighter) is fully
            // hidden — just the cave and the victor.
            ArenaBackground()
            Color.black.opacity(0.5)

            VStack(spacing: 18) {
                Text(title)
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
                        Text("\(tierName(tier)) — \(exitBlurb(tier))")
                            .font(.headline)
                            .foregroundStyle(.white.opacity(0.85))
                        if xpGained > 0 {
                            Label("+\(xpGained) XP", systemImage: "sparkles")
                                .font(.title3.bold())
                                .foregroundStyle(.white)
                        }
                        if let itemDropped {
                            // The reason to have pushed past the bank: the boss is
                            // the only thing down here that widens the loadout.
                            Label(
                                "\(itemDropped.displayName) — +\(itemDropped.stat.displayName), \(itemDropped.slot.displayName)",
                                systemImage: "shippingbox.fill"
                            )
                            .font(.callout.bold())
                            .foregroundStyle(.yellow)
                            .help(itemDropped.flavor)
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
            PetCombatSprite(family: petFamily, pose: .victory, size: 150)
        } else {
            FoeSprite(foeName: foeName, pose: .attack, size: 150)
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
        // Display name → asset base. The Scamp's art files are still `mote-*`.
        case "Dungeon Scamp": return "mote"
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
                        .frame(maxWidth: 210)
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
