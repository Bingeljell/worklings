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
    let family: PetFamily

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

            Ellipse()
                .fill(.black.opacity(0.28))
                .frame(width: 96, height: 18)
                .blur(radius: 5)
                .offset(y: 58)

            WorklingSprite(family: family, frame: .idle, size: 150)
        }
        .frame(height: 190)
        .overlay(
            RoundedRectangle(cornerRadius: 14)
                .stroke(.white.opacity(0.09), lineWidth: 1)
        )
        .accessibilityLabel("\(family.displayName) Workling")
    }
}

/// One gear slot: what's in it, what that's worth, and a menu of everything owned
/// that fits. The slot's fantasy rides along in the tooltip so an empty slot still
/// tells you what it's *for*.
private struct GearSlotButton: View {
    @ObservedObject var session: PetSession
    let slot: ItemSlot
    let pricing: GearPricing

    var body: some View {
        let equipped = session.state.loadout[slot]
        let owned = session.state.availableItems(for: slot)

        Menu {
            ForEach(owned, id: \.self) { item in
                Button(pricing.menuLabel(for: item)) { session.equip(item, in: slot) }
            }
            if !owned.isEmpty {
                Divider()
            }
            Button("Leave empty") { session.equip(nil, in: slot) }
        } label: {
            HStack(spacing: 10) {
                Image(systemName: icon)
                    .font(.system(size: 15))
                    .frame(width: 22)
                    .foregroundStyle(equipped == nil ? .secondary : .primary)

                VStack(alignment: .leading, spacing: 1) {
                    Text(slot.displayName.uppercased())
                        .font(.system(size: 9, weight: .heavy))
                        .foregroundStyle(.secondary)
                    Text(equipped?.displayName ?? "Empty")
                        .font(.caption.bold())
                        .foregroundStyle(equipped == nil ? .secondary : .primary)
                        .lineLimit(1)
                }

                Spacer(minLength: 4)

                if let equipped {
                    Text(pricing.priceLabel(for: equipped))
                        .font(.system(size: 10, weight: .bold, design: .monospaced))
                        .foregroundStyle(.green)
                }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity)
            .background(
                RoundedRectangle(cornerRadius: 9)
                    .fill(.quaternary.opacity(equipped == nil ? 0.4 : 0.9))
            )
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .help(equipped.map { "\($0.flavor)\n\n\(slot.fantasy)" } ?? slot.fantasy)
        .accessibilityLabel(
            "\(slot.displayName) slot, \(equipped?.displayName ?? "empty")"
        )
    }

    private var icon: String {
        switch slot {
        case .tool: "wrench.and.screwdriver.fill"
        case .ward: "shield.fill"
        case .charm: "sparkle"
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
                    ForEach(session.state.ownedItems, id: \.self) { item in
                        tile(for: item)
                    }
                }
            }
        }
    }

    private func tile(for item: Item) -> some View {
        let isEquipped = session.state.loadout[item.slot] == item

        return Button {
            session.equip(isEquipped ? nil : item, in: item.slot)
        } label: {
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

                Text(item.slot.displayName.uppercased())
                    .font(.system(size: 8, weight: .heavy))
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(10)
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(.quaternary.opacity(isEquipped ? 0.9 : 0.4))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 10)
                    .stroke(.green.opacity(isEquipped ? 0.5 : 0), lineWidth: 1)
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

private func sectionTitle(_ text: String) -> some View {
    Text(text.uppercased())
        .font(.system(size: 10, weight: .heavy))
        .foregroundStyle(.secondary)
}
