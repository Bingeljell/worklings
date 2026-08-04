/// Gear: the three functional slots, the base item set, and the read-time fold
/// that turns base stats into sheet stats.
///
/// The founding rule (from `docs/design/progression.md`, restated in
/// `docs/design/items.md`): gear modifies **effective** stats at **read-time** and
/// never touches the persisted base numbers. A save stores only *which* items are
/// owned and equipped; every stat consumer folds the modifiers in when it reads.
/// So gear arrives as pure computation — no stat migration, ever, however much
/// the numbers move later.
///
/// ```text
/// baseStats      (persisted; level + class growth)
///   → + equipped item modifiers   = sheet stats   (the Stats tab, and combat)
///   → × condition effectiveness   = combat stats  (what the resolver reads)
/// ```
///
/// The family *lean* belongs at the same read-time step and is not built yet —
/// when it lands it folds in inside `PetStats.effective(...)` alongside the item
/// modifiers, without moving this seam.

// MARK: - Slots

/// The three v1 gear slots. They are **functional, not paper-doll**: Worklings are
/// creatures, so gear is never helmet/chestplate armour — it's the stuff a working
/// companion carries. Each slot has its own small fantasy so equipping feels like
/// a choice rather than a stat-stick swap.
///
/// The set is expected to grow (a companion trinket, a sigil, a consumable
/// loadout) but always toward more themed *functional* slots.
public enum ItemSlot: String, CaseIterable, Codable, Equatable, Sendable {
    case tool
    case ward
    case charm

    public var displayName: String {
        switch self {
        case .tool: "Tool"
        case .ward: "Ward"
        case .charm: "Charm"
        }
    }

    /// What the slot *is*, in one line — the "the thing that…" framing from the
    /// design. Surfaces read this so the fantasy lives with the slot rather than
    /// being retyped per screen.
    public var fantasy: String {
        switch self {
        case .tool: "The thing it works with — what you bring to the problem."
        case .ward: "The thing that keeps it safe — what you hide behind on a bad day."
        case .charm: "The thing that's just its own — personality, carried."
        }
    }
}

// MARK: - The base item set

/// The v1 base items: one favouring each primary stat, dual-coded to
/// work-artifacts the same way the bestiary and the class names are.
///
/// Deliberately **mono-stat and primaries-only** — they teach the equip loop and
/// make the stat sheet matter without a rarity, affix, or proc system, and they
/// touch primary stats rather than derived attributes, keeping the two-layer stat
/// model clean. Richer gear (multi-stat, on-hit riders, set bonuses) is a later
/// layer that slots into this same read-time model.
///
/// The design's sixth item — the Lucky Green-Build Coin (+Luck) — is deliberately
/// absent: Luck is the classless sixth stat and combat v1 defers it, so the coin
/// ships whenever `PetStatKind` grows a `luck` case, not before.
public enum Item: String, CaseIterable, Codable, Equatable, Sendable {
    case crackedWhetstone
    case dentedBuckler
    case warmBackupCoal
    case rubberDuck
    case quickstepCharm

    public var displayName: String {
        switch self {
        case .crackedWhetstone: "Cracked Whetstone"
        case .dentedBuckler: "Dented Buckler"
        case .warmBackupCoal: "Warm Backup-Coal"
        case .rubberDuck: "Rubber Duck"
        case .quickstepCharm: "Quickstep Charm"
        }
    }

    public var slot: ItemSlot {
        switch self {
        case .crackedWhetstone: .tool
        case .dentedBuckler, .warmBackupCoal: .ward
        case .rubberDuck, .quickstepCharm: .charm
        }
    }

    /// The single primary stat this item nudges.
    public var stat: PetStatKind {
        switch self {
        case .crackedWhetstone: .power
        case .dentedBuckler: .defense
        case .warmBackupCoal: .vitality
        case .rubberDuck: .wit
        case .quickstepCharm: .agility
        }
    }

    /// The one family whose primary class leans on this item's stat, which reads a
    /// slightly larger modifier — the soft synergy rider. The mapping is exact
    /// because the family→class→stat matrix is 1:1.
    ///
    /// Nil where that family is still design-only: Guard attunes to **Bloomglass**
    /// and Agility to **Glitchkin**, neither of which exists in `PetFamily` yet, so
    /// those two items carry the universal base for everyone until the families
    /// ship. Attunement is soft anyway — a nudge, never a gate — so their absence
    /// costs correctness nothing.
    public var attunedFamily: PetFamily? {
        switch self {
        case .crackedWhetstone: .relicborn  // Power → Juggernaut
        case .rubberDuck: .elemental        // Wit → Tinkerer
        case .warmBackupCoal: .wildkin      // Vitality → Wellspring
        case .dentedBuckler: nil            // Guard → Bloomglass, not yet a family
        case .quickstepCharm: nil           // Agility → Glitchkin, not yet a family
        }
    }

    public var flavor: String {
        switch self {
        case .crackedWhetstone: "A worn edge still bites."
        case .dentedBuckler: "It has taken worse hits than you have."
        case .warmBackupCoal: "A little reserve, banked against a bad day."
        case .rubberDuck: "The oldest debugging tool there is; it listens."
        case .quickstepCharm: "Always half a step ahead."
        }
    }

    /// Every item that fits a slot, in declaration order — what a slot picker
    /// offers before filtering by what's actually owned.
    public static func all(in slot: ItemSlot) -> [Item] {
        allCases.filter { $0.slot == slot }
    }
}

// MARK: - Knobs

/// How big a gear nudge is. Same posture as `PetCombatRates`: first-pass alpha
/// tuning, retuned from real play without touching the mechanism.
///
/// The locked principle is that **gear is a nudge, not the dominant axis** —
/// builds and levels still lead. The defaults anchor to level-up growth, which
/// gives a class's signature stat +3 and every other stat +1 per level: an
/// unattuned item is worth less than one level of signature growth, and an
/// attuned one is worth exactly one. A full three-slot loadout therefore lands
/// near a level or two of progress, spread across three different stats — visible
/// on the sheet, never eclipsing it.
public struct ItemRates: Equatable, Sendable {
    /// Stat points every build reads from an equipped item.
    public let baseModifier: Int
    /// Extra points on top when the wearer's family attunes to the item.
    public let attunementBonus: Int

    public init(baseModifier: Int = 2, attunementBonus: Int = 1) {
        self.baseModifier = max(baseModifier, 0)
        self.attunementBonus = max(attunementBonus, 0)
    }

    /// What `item` is worth to a member of `family` — the universal base, plus the
    /// attunement rider when the family matches.
    public func modifier(for item: Item, family: PetFamily) -> Int {
        baseModifier + (item.attunedFamily == family ? attunementBonus : 0)
    }

    /// Whether this pairing reads the attunement rider — for a surface that wants
    /// to mark the thematic match, without re-deriving the comparison.
    public func isAttuned(_ item: Item, family: PetFamily) -> Bool {
        item.attunedFamily == family
    }
}

// MARK: - Loadout

/// What's equipped, one item per slot. Swapping is free — like class and family
/// today — until there's a reason to cost it.
public struct Loadout: Codable, Equatable, Sendable {
    public let tool: Item?
    public let ward: Item?
    public let charm: Item?

    public static let empty = Loadout()

    /// An item only ever sits in its own slot, so a mismatched value is dropped
    /// rather than silently granting its stat from the wrong place. That makes a
    /// hand-edited or future-written save self-correcting on read.
    public init(tool: Item? = nil, ward: Item? = nil, charm: Item? = nil) {
        self.tool = tool?.slot == .tool ? tool : nil
        self.ward = ward?.slot == .ward ? ward : nil
        self.charm = charm?.slot == .charm ? charm : nil
    }

    /// Decoding routes through the validating initializer rather than assigning
    /// the stored properties directly, which synthesized `Decodable` would do —
    /// otherwise a save is the one path that could smuggle an item into the wrong
    /// slot. `PetState` re-checks this too, but a `Loadout` decoded on its own
    /// should hold the same invariant.
    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            tool: try container.decodeIfPresent(Item.self, forKey: .tool),
            ward: try container.decodeIfPresent(Item.self, forKey: .ward),
            charm: try container.decodeIfPresent(Item.self, forKey: .charm)
        )
    }

    public subscript(slot: ItemSlot) -> Item? {
        switch slot {
        case .tool: tool
        case .ward: ward
        case .charm: charm
        }
    }

    /// The equipped items, skipping empty slots.
    public var equipped: [Item] {
        [tool, ward, charm].compactMap { $0 }
    }

    public var isEmpty: Bool { equipped.isEmpty }

    /// Equips `item` in `slot`, or clears the slot when it's nil. An item that
    /// doesn't belong to `slot` is rejected and the loadout comes back unchanged —
    /// the same forgiving posture as `PetState.renamed(to:)`, so a caller can try
    /// without first duplicating the validation.
    public func equipping(_ item: Item?, in slot: ItemSlot) -> Loadout {
        if let item, item.slot != slot { return self }
        return Loadout(
            tool: slot == .tool ? item : tool,
            ward: slot == .ward ? item : ward,
            charm: slot == .charm ? item : charm
        )
    }

    /// Equips `item` into the slot it belongs to, replacing whatever was there.
    public func equipping(_ item: Item) -> Loadout {
        equipping(item, in: item.slot)
    }

    public func clearing(_ slot: ItemSlot) -> Loadout {
        equipping(nil, in: slot)
    }

    /// The per-stat totals the equipped items contribute, for a surface that wants
    /// to show the delta ("+3 Power") rather than only the folded result. The
    /// effective-stat fold reads this too, so the sheet and combat can never
    /// disagree about what gear is worth.
    public func modifiers(
        family: PetFamily,
        rates: ItemRates = ItemRates()
    ) -> [PetStatKind: Int] {
        var totals: [PetStatKind: Int] = [:]
        for item in equipped {
            totals[item.stat, default: 0] += rates.modifier(for: item, family: family)
        }
        return totals
    }
}

// MARK: - The read-time fold

extension PetStats {
    /// Sheet stats: the persisted base plus everything equipped. This is the one
    /// place gear enters the numbers — combat, the stats panel, and any future
    /// readout all come through here, so nothing can read a stale or un-geared
    /// sheet. The family lean folds in here too when it's built.
    public func effective(
        loadout: Loadout,
        family: PetFamily,
        rates: ItemRates = ItemRates()
    ) -> PetStats {
        guard !loadout.isEmpty else { return self }
        let bonus = loadout.modifiers(family: family, rates: rates)
        func total(_ stat: PetStatKind) -> Int {
            value(for: stat) + (bonus[stat] ?? 0)
        }
        return PetStats(
            vitality: total(.vitality),
            power: total(.power),
            defense: total(.defense),
            agility: total(.agility),
            wit: total(.wit)
        )
    }
}
