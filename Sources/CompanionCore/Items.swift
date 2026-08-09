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

// MARK: - Tiers

/// How good a piece of gear is, which is the same question as **how deep you had
/// to go for it**.
///
/// Tiers exist because a delve that pays out once, at the very bottom, asks four
/// fights of a player and answers with a single item that might not even suit
/// their build. Every encounter now yields something, and the depth decides what:
/// the shallow fights hand over scavenged junk, the last regular encounter
/// something solid, and the mini-boss the genuinely good stuff. The gradient *is*
/// the reward for pushing.
public enum ItemTier: String, CaseIterable, Codable, Equatable, Sendable, Comparable {
    /// Off the early encounters — real, but barely.
    case scavenged
    /// The workaday tier; the original base set, and what a new Workling starts with.
    case solid
    /// Boss-only. The reason to walk past the bank prompt.
    case prime

    public var displayName: String {
        switch self {
        case .scavenged: "Scavenged"
        case .solid: "Solid"
        case .prime: "Prime"
        }
    }

    /// Ordering by worth, so surfaces can sort an inventory best-first and the
    /// drop logic can talk about "deeper" without a lookup table.
    private var rank: Int {
        switch self {
        case .scavenged: 0
        case .solid: 1
        case .prime: 2
        }
    }

    public static func < (lhs: ItemTier, rhs: ItemTier) -> Bool {
        lhs.rank < rhs.rank
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
    // Power → Tool
    case chippedFile
    case crackedWhetstone
    case mastersHone
    // Guard → Ward
    case bentPotLid
    case dentedBuckler
    case failsafePlate
    // Vitality → Ward
    case coldCoffeeDregs
    case warmBackupCoal
    case everburningBackup
    // Wit → Charm
    case stickyNote
    case rubberDuck
    case rootCauseLens
    // Agility → Charm
    case frayedLanyard
    case quickstepCharm
    case hotpathSigil

    public var displayName: String {
        switch self {
        case .chippedFile: "Chipped File"
        case .crackedWhetstone: "Cracked Whetstone"
        case .mastersHone: "Master's Hone"
        case .bentPotLid: "Bent Pot Lid"
        case .dentedBuckler: "Dented Buckler"
        case .failsafePlate: "Failsafe Plate"
        case .coldCoffeeDregs: "Cold Coffee Dregs"
        case .warmBackupCoal: "Warm Backup-Coal"
        case .everburningBackup: "Everburning Backup"
        case .stickyNote: "Sticky Note"
        case .rubberDuck: "Rubber Duck"
        case .rootCauseLens: "Root-Cause Lens"
        case .frayedLanyard: "Frayed Lanyard"
        case .quickstepCharm: "Quickstep Charm"
        case .hotpathSigil: "Hotpath Sigil"
        }
    }

    /// The stat an item favours fixes its slot, so a tier is always a
    /// like-for-like upgrade: a better Tool competes with your current Tool.
    public var slot: ItemSlot {
        switch stat {
        case .power: .tool
        case .defense, .vitality: .ward
        case .wit, .agility: .charm
        }
    }

    /// The single primary stat this item nudges.
    public var stat: PetStatKind {
        switch self {
        case .chippedFile, .crackedWhetstone, .mastersHone: .power
        case .bentPotLid, .dentedBuckler, .failsafePlate: .defense
        case .coldCoffeeDregs, .warmBackupCoal, .everburningBackup: .vitality
        case .stickyNote, .rubberDuck, .rootCauseLens: .wit
        case .frayedLanyard, .quickstepCharm, .hotpathSigil: .agility
        }
    }

    /// How deep you had to go to get it, and therefore how much it's worth.
    public var tier: ItemTier {
        switch self {
        case .chippedFile, .bentPotLid, .coldCoffeeDregs, .stickyNote, .frayedLanyard:
            .scavenged
        case .crackedWhetstone, .dentedBuckler, .warmBackupCoal, .rubberDuck, .quickstepCharm:
            .solid
        case .mastersHone, .failsafePlate, .everburningBackup, .rootCauseLens, .hotpathSigil:
            .prime
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
        switch stat {
        case .power: .relicborn     // Juggernaut
        case .wit: .elemental       // Tinkerer
        case .vitality: .wildkin    // Wellspring
        case .defense: nil          // Bloomglass, not yet a family
        case .agility: nil          // Glitchkin, not yet a family
        }
    }

    public var flavor: String {
        switch self {
        case .chippedFile: "It takes more passes. It still takes."
        case .crackedWhetstone: "A worn edge still bites."
        case .mastersHone: "Edges leave it hungry."
        case .bentPotLid: "Held wrong, it still holds."
        case .dentedBuckler: "It has taken worse hits than you have."
        case .failsafePlate: "Nothing gets through it without filing a report."
        case .coldCoffeeDregs: "Bitter. Still fuel."
        case .warmBackupCoal: "A little reserve, banked against a bad day."
        case .everburningBackup: "It outlived the outage that took everything else."
        case .stickyNote: "Someone wrote the answer down. It was you."
        case .rubberDuck: "The oldest debugging tool there is; it listens."
        case .rootCauseLens: "It shows you the actual problem, not the loud one."
        case .frayedLanyard: "Light enough to forget you're wearing it."
        case .quickstepCharm: "Always half a step ahead."
        case .hotpathSigil: "The shortest way, already known."
        }
    }

    /// Every item of a given tier — what a drop at a given depth chooses from.
    public static func all(in tier: ItemTier) -> [Item] {
        allCases.filter { $0.tier == tier }
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
/// gives a class's signature stat +3 and every other stat +1 per level: a Solid
/// item is worth less than one level of signature growth unattuned, and exactly
/// one attuned. Scavenged sits below that (a level of *incidental* growth), and
/// Prime above it — a boss item should be felt, since four fights and a declined
/// bank prompt is what it costs. Even a full Prime loadout is about three levels
/// of growth spread over three stats, so the ceiling stays a nudge.
public struct ItemRates: Equatable, Sendable {
    /// Stat points an equipped **Scavenged** item is worth to any build.
    public let scavengedModifier: Int
    /// Stat points an equipped **Solid** item is worth — the original base number,
    /// which the whole "gear is a nudge" anchoring was set against.
    public let solidModifier: Int
    /// Stat points an equipped **Prime** item is worth. Boss-only, so it is
    /// allowed to be worth walking past the bank prompt for.
    public let primeModifier: Int
    /// Extra points on top when the wearer's family attunes to the item.
    public let attunementBonus: Int

    public init(
        scavengedModifier: Int = 1,
        solidModifier: Int = 2,
        primeModifier: Int = 4,
        attunementBonus: Int = 1
    ) {
        self.scavengedModifier = max(scavengedModifier, 0)
        self.solidModifier = max(solidModifier, 0)
        self.primeModifier = max(primeModifier, 0)
        self.attunementBonus = max(attunementBonus, 0)
    }

    /// The universal base for a tier, before attunement.
    public func baseModifier(for tier: ItemTier) -> Int {
        switch tier {
        case .scavenged: scavengedModifier
        case .solid: solidModifier
        case .prime: primeModifier
        }
    }

    /// What `item` is worth to a member of `family` — its tier's universal base,
    /// plus the attunement rider when the family matches.
    public func modifier(for item: Item, family: PetFamily) -> Int {
        baseModifier(for: item.tier) + (item.attunedFamily == family ? attunementBonus : 0)
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

// MARK: - Swapping one item for another

/// What equipping an item would actually change, given what's already in its
/// slot.
///
/// A drop that only announces itself ("Quickstep Charm, +2 Agility") asks the
/// player to remember what's in that slot and do the subtraction. Since items are
/// mono-stat and slot-bound, the honest answer is usually **two** numbers moving
/// in different directions — gaining Agility while losing the Wit the Rubber Duck
/// was providing — which is exactly the comparison a single "+2" hides.
public struct GearSwap: Equatable, Sendable {
    /// One stat moving by one amount — the two halves of a swap, kept as data so
    /// a surface can style a gain and a loss differently.
    public struct StatDelta: Equatable, Sendable {
        public let stat: PetStatKind
        public let amount: Int

        public init(stat: PetStatKind, amount: Int) {
            self.stat = stat
            self.amount = amount
        }
    }

    public let incoming: Item
    /// What's currently in the slot, if anything.
    public let outgoing: Item?
    /// What the incoming item is worth to this wearer.
    public let gained: StatDelta
    /// What the outgoing item was worth, and would stop providing. Nil when the
    /// slot is empty — the case where a drop is pure upside.
    public let lost: StatDelta?

    public init(
        incoming: Item,
        outgoing: Item?,
        gained: StatDelta,
        lost: StatDelta?
    ) {
        self.incoming = incoming
        self.outgoing = outgoing
        self.gained = gained
        self.lost = lost
    }

    /// Whether the slot was empty, so equipping costs nothing.
    public var fillsEmptySlot: Bool { outgoing == nil }

    /// Whether the swap is the same item coming back — nothing moves.
    public var isNoOp: Bool { incoming == outgoing }

    /// The net change *on the incoming item's own stat*. Meaningful only when
    /// both items touch the same stat; otherwise the two deltas are the story and
    /// this is just the gain.
    public var netOnGainedStat: Int {
        guard let lost, lost.stat == gained.stat else { return gained.amount }
        return gained.amount - lost.amount
    }
}

extension Loadout {
    /// What equipping `item` would do to this loadout, for the wearer's family.
    /// The slot is the item's own — an item always knows where it goes.
    public func swap(
        to item: Item,
        family: PetFamily,
        rates: ItemRates = ItemRates()
    ) -> GearSwap {
        let outgoing = self[item.slot]
        return GearSwap(
            incoming: item,
            outgoing: outgoing,
            gained: GearSwap.StatDelta(
                stat: item.stat,
                amount: rates.modifier(for: item, family: family)
            ),
            lost: outgoing.map {
                GearSwap.StatDelta(
                    stat: $0.stat,
                    amount: rates.modifier(for: $0, family: family)
                )
            }
        )
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
