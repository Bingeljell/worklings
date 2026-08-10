import CompanionCore
import SwiftUI

/// The Character Screen: the home-base hub between idle companion and dungeon.
///
/// Its shape is a modern RPG character sheet — a persistent left rail holding the
/// Workling itself and what it's wearing, and a tabbed pane for everything you'd
/// read *about* it. The rail is deliberately not a tab: equipping is the thing
/// this screen exists to make tactile, so the gear must stay on screen while you
/// browse the inventory that fills it.
///
/// Layout is free-reflow by design (see the design notes): it is almost entirely
/// vector UI, and the one stretchable element — the model bay — is exactly what
/// becomes a live 3D view later, so there is nothing here that resizing can
/// pixelate.
struct CharacterScreenView: View {
    @ObservedObject var session: PetSession
    @State private var selectedTab: CharacterTab = .character

    /// The XP-rate line ("Learning at N% …") is derived from live needs rather
    /// than from the sheet, and is passed down beside it: it belongs under the XP
    /// bar because it explains why that bar is moving at the speed it is.
    private var learningRateLabel: String {
        PetPresentation.learningRateLabel(for: state)
    }

    private var state: PetState { session.state }

    private var sheet: CharacterSheet {
        CharacterSheet.make(
            state: state,
            combatRates: session.combatRates,
            itemRates: session.itemRates
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            CharacterHeader(session: session, sheet: sheet)
                .padding(.horizontal, 20)
                .padding(.top, 16)
                .padding(.bottom, 12)

            Divider()

            HStack(alignment: .top, spacing: 0) {
                GearRail(session: session)
                    .frame(width: 220)
                    .padding(.vertical, 18)
                    .padding(.horizontal, 16)

                Divider()

                VStack(alignment: .leading, spacing: 14) {
                    Picker("", selection: $selectedTab) {
                        ForEach(CharacterTab.allCases, id: \.self) { tab in
                            Text(tab.title).tag(tab)
                        }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .accessibilityLabel("Character screen section")

                    ScrollView {
                        Group {
                            switch selectedTab {
                            case .character:
                                StatsTabView(sheet: sheet, learningRateLabel: learningRateLabel)
                            case .inventory:
                                InventoryTabView(session: session)
                            case .skills:
                                SkillsTabView(sheet: sheet)
                            case .care:
                                CareTabView(session: session)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.bottom, 8)
                    }
                }
                .padding(.vertical, 18)
                .padding(.horizontal, 20)
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            if let warning = session.persistenceWarning {
                Divider()
                Label(warning, systemImage: "exclamationmark.triangle.fill")
                    .font(.caption)
                    .foregroundStyle(.orange)
                    .padding(.horizontal, 20)
                    .padding(.vertical, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(minWidth: 720, minHeight: 480)
        .background(.background)
    }
}

private enum CharacterTab: String, CaseIterable {
    case character
    case inventory
    case skills
    case care

    var title: String {
        switch self {
        case .character: "Character"
        case .inventory: "Inventory"
        case .skills: "Skills"
        case .care: "Care"
        }
    }
}

// MARK: - Header

/// Name, family, and standing — the identity line. Renaming lives here because
/// this screen is now where a Workling's identity is read, so it's also where
/// you'd expect to change it.
private struct CharacterHeader: View {
    @ObservedObject var session: PetSession
    let sheet: CharacterSheet

    @State private var isEditingName = false
    @State private var draftName = ""
    @FocusState private var nameFieldIsFocused: Bool

    private var presentation: PetPresentation {
        PetPresentation.make(state: session.state, reaction: session.reaction)
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            if isEditingName {
                nameEditor
            } else {
                Text(sheet.name)
                    .font(.system(size: 26, weight: .bold, design: .rounded))
                Button {
                    draftName = sheet.name
                    isEditingName = true
                    nameFieldIsFocused = true
                } label: {
                    Image(systemName: "pencil")
                        .font(.system(size: 12))
                }
                .buttonStyle(.borderless)
                .help("Rename \(sheet.name).")
            }

            Text("Level \(sheet.level) \(sheet.petClass.displayName)")
                .font(.system(size: 13, weight: .semibold, design: .rounded))
                .foregroundStyle(.secondary)

            Spacer()

            Text(sheet.family.displayName)
                .font(.system(size: 12, weight: .bold, design: .rounded))
                .padding(.horizontal, 9)
                .padding(.vertical, 4)
                .background(.blue.opacity(0.15), in: Capsule())

            Text(presentation.moodLabel)
                .font(.system(size: 12, weight: .bold, design: .rounded))
                .padding(.horizontal, 9)
                .padding(.vertical, 4)
                .background(.purple.opacity(0.15), in: Capsule())
        }
    }

    private var nameEditor: some View {
        HStack(spacing: 6) {
            TextField("Name", text: $draftName)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 18, weight: .bold, design: .rounded))
                .focused($nameFieldIsFocused)
                .onSubmit(commitRename)
                .frame(maxWidth: 170)

            Button {
                commitRename()
            } label: {
                Image(systemName: "checkmark.circle.fill")
            }
            .buttonStyle(.borderless)
            .disabled(!PetState.isValidName(draftName))

            Button {
                isEditingName = false
            } label: {
                Image(systemName: "xmark.circle.fill")
            }
            .buttonStyle(.borderless)
        }
    }

    private func commitRename() {
        guard PetState.isValidName(draftName) else { return }
        session.rename(to: draftName)
        isEditingName = false
    }
}

// MARK: - The rail: model bay + gear

/// The Workling and the three things it carries, always visible. Equipping from
/// the Inventory tab changes what's shown here immediately, which is the whole
/// reason the rail isn't itself a tab.
private struct GearRail: View {
    @ObservedObject var session: PetSession

    private var pricing: GearPricing { GearPricing(session: session) }

    var body: some View {
        VStack(spacing: 14) {
            ModelBay(family: session.state.family)

            VStack(spacing: 8) {
                ForEach(ItemSlot.allCases, id: \.self) { slot in
                    GearSlotButton(session: session, slot: slot, pricing: pricing)
                }
            }

            loadoutTotal

            Spacer(minLength: 0)
        }
    }

    private var loadoutTotal: some View {
        let parts = pricing.statLineParts(for: session.state.loadout)
        let attunement = pricing.attunementExplanation(for: session.state.loadout)

        return VStack(spacing: 3) {
            if parts.isEmpty {
                Text("Nothing equipped")
                    .foregroundStyle(.secondary)
            } else {
                // Labelled, because an unheaded "+2 Wit" under three slots reads as
                // a stray number rather than the sum of what's in them.
                Text("GEAR TOTAL")
                    .font(.system(size: 8, weight: .heavy))
                    .foregroundStyle(.secondary)
                    .tracking(0.8)
                Text(parts.joined(separator: " · "))
                    .foregroundStyle(.green)
                    .multilineTextAlignment(.center)
                if let attunement {
                    Text("✦ attuned")
                        .foregroundStyle(.orange)
                        .help(attunement)
                }
            }
        }
        .font(.caption2.bold())
        .accessibilityElement(children: .combine)
        .accessibilityLabel(
            parts.isEmpty ? "Nothing equipped" : "Gear total: " + parts.joined(separator: ", ")
        )
    }
}

/// Phase 1 of the model bay: a static render of the Workling. The frame, the
/// backdrop, and the space it claims are already the final ones, so swapping in a
/// live rotatable `SceneView` later is a change of contents, not of layout.
private struct ModelBay: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    let family: PetFamily

    /// Drives the breathing bob. Separate from the blink timeline because a bob
    /// sampled at blink cadence would step rather than breathe.
    @State private var breathing = false

    /// How often the sprite is resampled. Fine enough that a blink can be short.
    private static let tickSeconds: Double = 0.15
    /// Ticks in one blink cycle — a single-tick blink roughly every three seconds,
    /// rather than the desktop companion's half-open/half-shut alternation, which
    /// reads as a strobe at this size.
    private static let ticksPerBlinkCycle = 20

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 14)
                .fill(
                    LinearGradient(
                        colors: [
                            Color(red: 0.16, green: 0.15, blue: 0.28),
                            Color(red: 0.09, green: 0.09, blue: 0.17)
                        ],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )

            // The contact shadow tightens as the Workling rises, which is what
            // sells the bob as weight rather than the whole image sliding.
            Ellipse()
                .fill(.black.opacity(0.28))
                .frame(width: breathing ? 88 : 96, height: 18)
                .blur(radius: 5)
                .offset(y: 58)

            TimelineView(.periodic(from: .now, by: Self.tickSeconds)) { context in
                WorklingSprite(family: family, frame: frame(at: context.date), size: 150)
            }
            .offset(y: breathing ? -3 : 1)
        }
        .onAppear {
            guard !reduceMotion else { return }
            withAnimation(.easeInOut(duration: 2.1).repeatForever(autoreverses: true)) {
                breathing = true
            }
        }
        .frame(height: 190)
        .overlay(
            RoundedRectangle(cornerRadius: 14)
                .stroke(.white.opacity(0.09), lineWidth: 1)
        )
        .accessibilityLabel("\(family.displayName) Workling")
    }

    /// Idle, with an occasional blink. Reduce Motion holds the open-eyed frame.
    private func frame(at date: Date) -> WorklingSpriteFrame {
        guard !reduceMotion else { return .idle }
        let tick = Int(date.timeIntervalSinceReferenceDate / Self.tickSeconds)
        return tick % Self.ticksPerBlinkCycle == 0 ? .idleBlink : .idle
    }
}

/// One gear slot: what's in it, what that's worth, and a menu of everything owned
/// that fits. The slot's fantasy rides along in the tooltip so an empty slot still
/// tells you what it's *for*.
///
/// The layout is the RPG paper-doll one: a labelled row per slot with a real
/// **square slot box** on the right, because that is the shape people already read
/// as "gear goes here". An empty box is a recessed dashed well; a filled one is a
/// tier-tinted plate carrying the item's glyph, ringed in green with a corner
/// check. Naming the item and its stat under the label answers the other half —
/// previously the rail showed a bare "+2 Wit" total with nothing saying *which*
/// item was providing it.
///
/// The box is deliberately sized and shaped for a real item image to drop into
/// later; the glyph inside is the placeholder, and swapping it for art is a change
/// of contents, not of layout.
private struct GearSlotButton: View {
    @ObservedObject var session: PetSession
    let slot: ItemSlot
    let pricing: GearPricing

    @State private var isPicking = false

    private static let boxSize: CGFloat = 46

    var body: some View {
        let equipped = session.state.loadout[slot]
        let tint = equipped.map { tierTint($0.tier) } ?? .secondary

        // A plain Button, *not* a Menu. On macOS a SwiftUI `Menu` label is backed
        // by NSPopUpButton, which flattens the label down to a single image plus a
        // single title and silently discards everything else — which is why this
        // row rendered as an icon and the word TOOL no matter what was written
        // into it. The picker moves into a popover, where it can also show what
        // it's offering.
        Button {
            isPicking = true
        } label: {
            HStack(alignment: .center, spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 5) {
                        Image(systemName: emptySlotIcon(slot))
                            .font(.system(size: 11))
                            .foregroundStyle(.secondary)
                        Text(slot.displayName.uppercased())
                            .font(.system(size: 9, weight: .heavy))
                            .foregroundStyle(.secondary)
                            .tracking(0.7)
                    }

                    // Which item this is. The whole point of the row.
                    Text(equipped?.displayName ?? "Empty")
                        .font(.caption.bold())
                        .foregroundStyle(equipped == nil ? .tertiary : .primary)
                        .lineLimit(1)
                        .truncationMode(.tail)

                    if let equipped {
                        HStack(spacing: 4) {
                            Text(pricing.priceLabel(for: equipped))
                                .font(.system(size: 10, weight: .bold, design: .monospaced))
                                .foregroundStyle(.green)
                            Text(equipped.tier.displayName.uppercased())
                                .font(.system(size: 8, weight: .heavy))
                                .padding(.horizontal, 4)
                                .padding(.vertical, 1)
                                .background(Capsule().fill(tint.opacity(0.22)))
                                .foregroundStyle(tint)
                        }
                    }
                }

                Spacer(minLength: 4)

                slotBox(equipped: equipped, tint: tint)
            }
            .padding(.horizontal, 9)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .popover(isPresented: $isPicking, arrowEdge: .trailing) {
            GearSlotPicker(session: session, slot: slot, pricing: pricing, isPresented: $isPicking)
        }
        .help(equipped.map { "\($0.flavor)\n\n\(slot.fantasy)" } ?? slot.fantasy)
        .accessibilityLabel(
            equipped.map { "\(slot.displayName) slot, equipped: \($0.displayName), \(pricing.priceLabel(for: $0))" }
                ?? "\(slot.displayName) slot, empty"
        )
    }

    /// The slot box itself — the thing you can read across the room. Empty is a
    /// recessed dashed well holding a ghosted slot glyph; equipped is a tinted
    /// plate with a green ring and a corner check, so occupancy survives being
    /// glanced at, screenshotted, or seen by someone who can't separate the tier
    /// colours.
    @ViewBuilder
    private func slotBox(equipped: Item?, tint: Color) -> some View {
        ZStack {
            if let equipped {
                RoundedRectangle(cornerRadius: 9).fill(tint.opacity(0.2))
                RoundedRectangle(cornerRadius: 9)
                    .strokeBorder(.green.opacity(0.8), lineWidth: 2)
                Image(systemName: itemIcon(equipped))
                    .font(.system(size: 20, weight: .semibold))
                    .foregroundStyle(tint)
            } else {
                RoundedRectangle(cornerRadius: 9).fill(.black.opacity(0.22))
                RoundedRectangle(cornerRadius: 9)
                    .strokeBorder(
                        .tertiary,
                        style: StrokeStyle(lineWidth: 1.5, dash: [4, 3])
                    )
                Image(systemName: emptySlotIcon(slot))
                    .font(.system(size: 16))
                    .foregroundStyle(.quaternary)
            }
        }
        .frame(width: Self.boxSize, height: Self.boxSize)
        .overlay(alignment: .topTrailing) {
            if equipped != nil {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 12))
                    .foregroundStyle(.green)
                    .background(Circle().fill(.background))
                    .offset(x: 5, y: -5)
            }
        }
    }

}

// MARK: - Character tab

/// The stat sheet, showing the whole ladder: what the Workling has grown, what
/// gear adds on top, and what it actually walks into a fight with once condition
/// is applied. Three rungs, because hiding any one of them would hide either the
/// value of the gear or the cost of neglect.
private struct StatsTabView: View {
    let sheet: CharacterSheet
    /// How fast XP is currently accruing, given condition — the one plain line
    /// that makes the care→progression coupling legible instead of something a
    /// player reverse-engineers from grants that look smaller than they should.
    let learningRateLabel: String

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            levelSummary

            VStack(alignment: .leading, spacing: 8) {
                sectionTitle("Stats")
                HStack(spacing: 8) {
                    Text("").frame(maxWidth: .infinity, alignment: .leading)
                    Text("BASE").frame(width: 46, alignment: .trailing)
                    Text("GEAR").frame(width: 46, alignment: .trailing)
                    Text("TOTAL").frame(width: 52, alignment: .trailing)
                }
                .font(.system(size: 9, weight: .heavy))
                .foregroundStyle(.secondary)

                ForEach(sheet.rows, id: \.stat) { row in
                    statRow(row)
                }
            }

            combatReadout
        }
    }

    private var levelSummary: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text("Level \(sheet.level) \(sheet.petClass.displayName)")
                    .font(.system(size: 14, weight: .semibold, design: .rounded))
                Spacer()
                Text(sheet.petClass.role)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
            ProgressView(value: sheet.progress.fraction)
                .tint(.yellow)
            Text(
                "\(Int(sheet.progress.xpIntoLevel)) / \(Int(sheet.progress.xpForLevel)) XP "
                    + "to next level"
            )
            .font(.caption2)
            .foregroundStyle(.secondary)
            Text(learningRateLabel)
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(
            "Level \(sheet.level) \(sheet.petClass.displayName), \(sheet.petClass.role), "
                + "\(Int(sheet.progress.xpIntoLevel)) of \(Int(sheet.progress.xpForLevel)) XP "
                + "to next level, " + learningRateLabel
        )
    }

    private func statRow(_ row: CharacterSheet.StatRow) -> some View {
        HStack(spacing: 8) {
            HStack(spacing: 5) {
                Text(row.stat.displayName)
                    .font(.callout)
                    .fontWeight(row.isSignature ? .bold : .regular)
                if row.isSignature {
                    Image(systemName: "star.fill")
                        .font(.system(size: 8))
                        .foregroundStyle(.yellow)
                        .help("\(sheet.petClass.displayName)'s signature stat — it grows fastest.")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            Text("\(row.base)")
                .frame(width: 46, alignment: .trailing)
                .foregroundStyle(.secondary)
            Text(row.gearBonus > 0 ? "+\(row.gearBonus)" : "—")
                .frame(width: 46, alignment: .trailing)
                .foregroundStyle(row.gearBonus > 0 ? .green : .secondary)
            Text("\(row.effective)")
                .frame(width: 52, alignment: .trailing)
                .fontWeight(.semibold)
        }
        .font(.system(.callout, design: .monospaced))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(
            row.isSignature
                ? "\(row.stat.displayName), signature stat" : row.stat.displayName
        )
        .accessibilityValue(
            row.gearBonus > 0
                ? "\(row.effective), \(row.base) base plus \(row.gearBonus) from gear"
                : "\(row.effective)"
        )
    }

    /// What the numbers above turn into once condition scales them. Kept visually
    /// distinct from the stat table because it's a *different rung* — the sheet
    /// stats are permanent, these move with how well the Workling is looked after.
    private var combatReadout: some View {
        VStack(alignment: .leading, spacing: 8) {
            sectionTitle("In the arena")

            HStack(spacing: 10) {
                readoutTile("Max HP", "\(sheet.combat.maxHP)", icon: "heart.fill", tint: .red)
                readoutTile("Strike", "\(sheet.combat.strike)", icon: "burst.fill", tint: .orange)
                readoutTile(
                    "Crit",
                    "\(Int((sheet.combat.critChance * 100).rounded()))%",
                    icon: "bolt.fill",
                    tint: .yellow
                )
                readoutTile(
                    "Condition",
                    "\(Int((sheet.combat.effectiveness * 100).rounded()))%",
                    icon: "figure.run",
                    tint: sheet.combat.isDiminished ? .orange : .green
                )
            }

            if sheet.combat.isDiminished {
                Text(
                    "Condition is scaling these down — gear raises the ceiling, "
                        + "but care is what reaches it."
                )
                .font(.caption2)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func readoutTile(
        _ label: String,
        _ value: String,
        icon: String,
        tint: Color
    ) -> some View {
        VStack(spacing: 3) {
            Image(systemName: icon)
                .font(.system(size: 12))
                .foregroundStyle(tint)
            Text(value)
                .font(.system(size: 16, weight: .bold, design: .rounded))
            Text(label)
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: 9).fill(.quaternary.opacity(0.5)))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(label)
        .accessibilityValue(value)
    }
}

// MARK: - Inventory tab

/// Everything owned, whether or not it's worn. Clicking a tile equips it into its
/// own slot — the item knows where it goes, so there's no slot to choose — and
/// clicking what's already equipped takes it off.
private struct InventoryTabView: View {
    @ObservedObject var session: PetSession

    private var pricing: GearPricing { GearPricing(session: session) }

    private let columns = [GridItem(.adaptive(minimum: 190, maximum: 280), spacing: 10)]

    /// Best first, then by slot, so a hard-won Prime item isn't buried under the
    /// junk that happened to drop before it.
    private var sortedItems: [Item] {
        session.state.ownedItems.sorted { lhs, rhs in
            if lhs.tier != rhs.tier { return lhs.tier > rhs.tier }
            return lhs.slot.displayName < rhs.slot.displayName
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Click an item to equip it; click it again to take it off.")
                .font(.caption)
                .foregroundStyle(.secondary)

            if session.state.ownedItems.isEmpty {
                Text("Nothing carried yet. Gear comes off the bottom of a delve.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .padding(.vertical, 20)
            } else {
                LazyVGrid(columns: columns, alignment: .leading, spacing: 10) {
                    ForEach(sortedItems, id: \.self) { item in
                        tile(for: item)
                    }
                }
            }
        }
    }

    private func tile(for item: Item) -> some View {
        let isEquipped = session.state.loadout[item.slot] == item

        let tint = tierTint(item.tier)

        return Button {
            session.equip(isEquipped ? nil : item, in: item.slot)
        } label: {
            HStack(alignment: .top, spacing: 10) {
                // The item's own glyph, sized to anchor the row. Without it every
                // tile was an indistinguishable paragraph.
                ZStack {
                    RoundedRectangle(cornerRadius: 8).fill(tint.opacity(0.18))
                    RoundedRectangle(cornerRadius: 8).strokeBorder(tint.opacity(0.5), lineWidth: 1)
                    Image(systemName: itemIcon(item))
                        .font(.system(size: 18, weight: .semibold))
                        .foregroundStyle(tint)
                }
                .frame(width: 40, height: 40)
                .overlay(alignment: .bottomTrailing) {
                    if isEquipped {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 12))
                            .foregroundStyle(.green)
                            .background(Circle().fill(.background))
                            .offset(x: 4, y: 4)
                    }
                }

                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 6) {
                        Text(item.displayName)
                            .font(.caption.bold())
                            .lineLimit(1)
                        Spacer(minLength: 2)
                        if isEquipped {
                            Text("WORN")
                                .font(.system(size: 8, weight: .heavy))
                                .padding(.horizontal, 5)
                                .padding(.vertical, 2)
                                .background(Capsule().fill(.green.opacity(0.25)))
                        }
                    }

                    Text(pricing.priceLabel(for: item))
                        .font(.system(size: 11, weight: .bold, design: .monospaced))
                        .foregroundStyle(.green)

                    Text(item.flavor)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .fixedSize(horizontal: false, vertical: true)

                    HStack(spacing: 5) {
                        // How deep it came from, which is the same thing as how good
                        // it is — the one piece of an item's story the name alone
                        // doesn't carry.
                        Text(item.tier.displayName.uppercased())
                            .font(.system(size: 8, weight: .heavy))
                            .padding(.horizontal, 4)
                            .padding(.vertical, 1)
                            .background(Capsule().fill(tint.opacity(0.22)))
                            .foregroundStyle(tint)
                        Text(item.slot.displayName.uppercased())
                            .font(.system(size: 8, weight: .heavy))
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(10)
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(isEquipped ? AnyShapeStyle(tint.opacity(0.14)) : AnyShapeStyle(.quaternary.opacity(0.35)))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 10)
                    .stroke(isEquipped ? .green.opacity(0.65) : tint.opacity(0.25), lineWidth: 1)
            )
        }
        .buttonStyle(.plain)
        .help(
            pricing.isAttuned(item)
                ? "\(item.flavor)\n\nSuits a \(session.state.family.displayName) — a little extra."
                : item.flavor
        )
        .accessibilityLabel("\(item.displayName), \(item.slot.displayName) slot")
        .accessibilityValue(isEquipped ? "equipped" : "not equipped")
    }
}

// MARK: - Skills tab

/// The class's mechanical identity, as far as it exists today.
///
/// The ability *tree* is designed but unbuilt — there is no ability model in
/// `CompanionCore` yet — so rather than mock up a tree that can't be clicked, this
/// shows what genuinely drives the fight right now (the class, its role, its
/// signature stat) and says plainly where the rest is. The tab's frame is here so
/// the tree lands into a place that already exists.
private struct SkillsTabView: View {
    let sheet: CharacterSheet

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 5) {
                Text(sheet.petClass.displayName)
                    .font(.system(size: 18, weight: .bold, design: .rounded))
                Text(sheet.petClass.role)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }

            HStack(spacing: 6) {
                Image(systemName: "star.fill")
                    .font(.system(size: 10))
                    .foregroundStyle(.yellow)
                Text("Signature stat: \(sheet.petClass.signatureStat.displayName)")
                    .font(.callout)
                Text("— grows fastest every level")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Divider()

            Label(
                "The ability tree isn't built yet. For now a Workling fights on its "
                    + "stats and its Approach; skills land here when they do.",
                systemImage: "hammer.fill"
            )
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        }
    }
}

// MARK: - Shared

/// One colour per tier, shared by the inventory and the drop reveal so "Prime"
/// looks the same wherever it's named.
func tierTint(_ tier: ItemTier) -> Color {
    switch tier {
    case .scavenged: .gray
    case .solid: .cyan
    case .prime: .yellow
    }
}

/// The "what goes in this slot" picker, shown from a popover rather than a menu.
///
/// It exists as a real view because macOS flattens `Menu` labels *and* menu item
/// content: a menu could only ever offer a line of text per item. In a popover the
/// choice can be shown the same way the slot shows it — glyph, name, price, tier —
/// so picking gear looks like the screen it's picked on.
struct GearSlotPicker: View {
    @ObservedObject var session: PetSession
    let slot: ItemSlot
    let pricing: GearPricing
    @Binding var isPresented: Bool

    var body: some View {
        let owned = session.state.availableItems(for: slot)
        let equipped = session.state.loadout[slot]

        VStack(alignment: .leading, spacing: 6) {
            Text(slot.displayName.uppercased())
                .font(.system(size: 9, weight: .heavy))
                .foregroundStyle(.secondary)
                .tracking(0.7)
            Text(slot.fantasy)
                .font(.caption2)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: 230, alignment: .leading)

            Divider()

            if owned.isEmpty {
                Text("Nothing you carry fits here yet.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(owned, id: \.self) { item in
                    row(for: item, isEquipped: item == equipped)
                }
            }

            Divider()

            Button {
                session.equip(nil, in: slot)
                isPresented = false
            } label: {
                Label("Leave empty", systemImage: "xmark.circle")
                    .font(.caption)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(equipped == nil)
            .foregroundStyle(equipped == nil ? .tertiary : .secondary)
        }
        .padding(12)
        .frame(width: 258)
    }

    private func row(for item: Item, isEquipped: Bool) -> some View {
        let tint = tierTint(item.tier)

        return Button {
            session.equip(item, in: slot)
            isPresented = false
        } label: {
            HStack(spacing: 8) {
                ZStack {
                    RoundedRectangle(cornerRadius: 6).fill(tint.opacity(0.18))
                    Image(systemName: itemIcon(item))
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(tint)
                }
                .frame(width: 26, height: 26)

                VStack(alignment: .leading, spacing: 1) {
                    Text(item.displayName)
                        .font(.caption.bold())
                        .lineLimit(1)
                    Text(pricing.priceLabel(for: item))
                        .font(.system(size: 10, weight: .bold, design: .monospaced))
                        .foregroundStyle(.green)
                }

                Spacer(minLength: 4)

                if isEquipped {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 12))
                        .foregroundStyle(.green)
                }
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 5)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 7)
                    .fill(isEquipped ? AnyShapeStyle(.green.opacity(0.12)) : AnyShapeStyle(Color.clear))
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(item.flavor)
    }
}

/// One glyph per *item*, shared by the gear rail, the inventory, the briefing
/// loadout bar, and the drop reveal.
///
/// Deliberately per-item rather than per-slot: a slot glyph makes every Tool look
/// like every other Tool, which left the gear screens reading as rows of text with
/// a decorative bullet. Each item is dual-coded to a work-artifact, so each one has
/// an obvious symbol — the Rubber Duck listens, the Root-Cause Lens magnifies.
///
/// Lives in the app layer, not `CompanionCore`: the core is deliberately
/// platform-portable and knows nothing about SF Symbols.
func itemIcon(_ item: Item) -> String {
    switch item {
    // Power → Tool
    case .chippedFile: "wrench.fill"
    case .crackedWhetstone: "hammer.fill"
    case .mastersHone: "bolt.fill"
    // Guard → Ward
    case .bentPotLid: "shield.lefthalf.filled"
    case .dentedBuckler: "shield.fill"
    case .failsafePlate: "lock.shield.fill"
    // Vitality → Ward
    case .coldCoffeeDregs: "cup.and.saucer.fill"
    case .warmBackupCoal: "flame.fill"
    case .everburningBackup: "flame.circle.fill"
    // Wit → Charm
    case .stickyNote: "note.text"
    case .rubberDuck: "bird.fill"
    case .rootCauseLens: "magnifyingglass.circle.fill"
    // Agility → Charm
    case .frayedLanyard: "link"
    case .quickstepCharm: "hare.fill"
    case .hotpathSigil: "bolt.horizontal.fill"
    }
}

/// The glyph for an empty slot — the slot's own outline, so a gap still says what
/// belongs in it.
func emptySlotIcon(_ slot: ItemSlot) -> String {
    switch slot {
    case .tool: "wrench.and.screwdriver"
    case .ward: "shield"
    case .charm: "sparkle"
    }
}

private func sectionTitle(_ text: String) -> some View {
    Text(text.uppercased())
        .font(.system(size: 10, weight: .heavy))
        .foregroundStyle(.secondary)
}
