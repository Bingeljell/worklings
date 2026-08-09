import CompanionCore

/// What gear is *worth to this Workling*, in words.
///
/// Two surfaces now price the same items — the delve briefing's prep bar and the
/// Character Screen — and they must never disagree, because a player reading
/// "+3 Power ✦" in one place and "+2 Power" in the other would rightly stop
/// trusting both. The arithmetic already has one home (`ItemRates`); this gives
/// the *vocabulary* one home too, so the two screens differ only in chrome.
struct GearPricing {
    let family: PetFamily
    let rates: ItemRates

    init(family: PetFamily, rates: ItemRates) {
        self.family = family
        self.rates = rates
    }

    @MainActor
    init(session: PetSession) {
        self.init(family: session.state.family, rates: session.itemRates)
    }

    func bonus(for item: Item) -> Int {
        rates.modifier(for: item, family: family)
    }

    func isAttuned(_ item: Item) -> Bool {
        rates.isAttuned(item, family: family)
    }

    /// The attunement mark, or nothing — one symbol, defined once, so the two
    /// screens can't drift onto different glyphs.
    func attunementMark(for item: Item) -> String {
        isAttuned(item) ? " ✦" : ""
    }

    /// What one item is worth, priced where the choice is made: a menu row or an
    /// inventory tile reads the same.
    func priceLabel(for item: Item) -> String {
        "+\(bonus(for: item)) \(item.stat.displayName)\(attunementMark(for: item))"
    }

    /// The item's name with its price attached — the menu-row form.
    func menuLabel(for item: Item) -> String {
        "\(item.displayName)  \(priceLabel(for: item))"
    }

    /// What a whole loadout is worth, one part per stat it touches. Ordered by
    /// `PetStatKind.allCases` rather than by the loadout, so the readout doesn't
    /// reshuffle itself as items are swapped.
    func statLineParts(for loadout: Loadout) -> [String] {
        let modifiers = loadout.modifiers(family: family, rates: rates)
        return PetStatKind.allCases.compactMap { stat in
            guard let bonus = modifiers[stat], bonus > 0 else { return nil }
            return "+\(bonus) \(stat.displayName)"
        }
    }

    /// The tooltip behind the ✦ mark: which equipped items suit this family, and
    /// why that's worth anything.
    func attunementExplanation(for loadout: Loadout) -> String? {
        let attuned = loadout.equipped.filter(isAttuned)
        guard !attuned.isEmpty else { return nil }
        return attuned.map(\.displayName).joined(separator: ", ")
            + " suits a \(family.displayName) — a little extra."
    }
}
