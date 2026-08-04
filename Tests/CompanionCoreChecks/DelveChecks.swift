import CompanionCore
import Foundation

/// Checks for the `Delve` orchestrator — the chain wrapper around single
/// encounters: HP carry, inter-encounter regen, bank-vs-push, and the once-only
/// exit-tier write-back. Most checks drive the state machine directly (feeding
/// controlled outcomes) so they assert the orchestration precisely; one runs real
/// encounters end to end to confirm the delve replays deterministically.
enum DelveChecks {
    static func run(context: inout CheckContext) {
        checkStartsInBriefingThenDescends(context: &context)
        checkFullClearGrantsCompletionBonus(context: &context)
        checkBankKeepsAccruedXPButNoBonus(context: &context)
        checkHPCarriesAndRegensOnPush(context: &context)
        checkDownedRetreatIsTierDownedNoXP(context: &context)
        checkConditionDeltaAppliedExactlyOnce(context: &context)
        checkEndToEndDelveReplaysIdentically(context: &context)
        checkOnlyABossClearDropsGear(context: &context)
        checkDropIsNeverADuplicateAndDriesUp(context: &context)
    }

    // MARK: Drops

    /// Gear comes off the boss and nothing else — that, with the completion
    /// bonus, is exactly what banking forfeits.
    private static func checkOnlyABossClearDropsGear(context: inout CheckContext) {
        var cleared = cacheWarren(seed: 11)
        clearAll(&cleared, hpRemaining: 40)
        let bossRun = cleared.resolution(applyingTo: neutralState())
        context.expect(bossRun?.itemDropped != nil, "clearing the boss drops an item")
        if let drop = bossRun?.itemDropped {
            context.expect(
                bossRun?.state.ownedItems.contains(drop) == true,
                "the dropped item is in the inventory of the returned state"
            )
        }

        var banked = cacheWarren(seed: 11)
        clearAll(&banked, hpRemaining: 40, bankAfter: 1)
        context.expectEqual(
            banked.resolution(applyingTo: neutralState())?.itemDropped, nil,
            "banking early forfeits the drop along with the completion bonus"
        )

        var downed = cacheWarren(seed: 11)
        downed.descend()
        downed.recordOutcome(petVictory: false, petHPRemaining: 0)
        context.expectEqual(
            downed.resolution(applyingTo: neutralState())?.itemDropped, nil,
            "a retreat drops nothing"
        )

        // Same seed, same delve, same drop — a delve replays whole.
        var replay = cacheWarren(seed: 11)
        clearAll(&replay, hpRemaining: 40)
        context.expectEqual(
            replay.resolution(applyingTo: neutralState())?.itemDropped,
            bossRun?.itemDropped,
            "the drop is deterministic in the delve seed"
        )
    }

    /// A drop always widens the loadout: never a duplicate, and nil rather than
    /// a fake reward once the base set is complete.
    private static func checkDropIsNeverADuplicateAndDriesUp(context: inout CheckContext) {
        var delve = cacheWarren(seed: 7)
        clearAll(&delve, hpRemaining: 40)

        let owningMost = neutralState()
            .acquiring(.crackedWhetstone)
            .acquiring(.dentedBuckler)
            .acquiring(.warmBackupCoal)
            .acquiring(.quickstepCharm)
        // The starter Rubber Duck plus those four is the whole set bar none.
        let onlyOption = delve.resolution(applyingTo: owningMost)?.itemDropped
        context.expect(
            onlyOption == nil || !owningMost.ownedItems.contains(onlyOption!),
            "a drop is never something already owned"
        )

        var complete = owningMost
        for item in Item.allCases { complete = complete.acquiring(item) }
        context.expectEqual(
            delve.resolution(applyingTo: complete)?.itemDropped, nil,
            "with the base set complete there is nothing left to drop"
        )
        context.expectEqual(
            delve.resolution(applyingTo: complete)?.state.ownedItems.count,
            Item.allCases.count,
            "a dry drop leaves the inventory exactly as it was"
        )
    }

    // MARK: Fixtures

    private static let rates = PetCombatRates()

    // A champion strong enough to crush the whole Warren, so the end-to-end run
    // reliably clears every encounter for any seed. Vitality 20 → maxHP 80.
    private static let championStats = PetStats(
        vitality: 20, power: 20, defense: 15, agility: 15, wit: 10
    )
    private static let fullHealth = PetNeeds(
        hunger: 0, energy: 100, happiness: 100, trust: 100
    )

    private static func champion() -> Combatant {
        Combatant.pet(name: "Champ", baseStats: championStats, needs: fullHealth, rates: rates)
    }

    private static func cacheWarren(seed: UInt64) -> Delve {
        Delve.cacheWarren(pet: champion(), effectiveness: 1.0, rates: rates, baseSeed: seed)
    }

    // A neutral state to receive the write-back — needs at 50 so tier deltas stay
    // inside the clamp and are easy to read.
    private static func neutralState() -> PetState {
        PetState.newPet(now: Date(timeIntervalSinceReferenceDate: 0))
            .applying(needs: PetNeeds(hunger: 50, energy: 50, happiness: 50, trust: 50))
    }

    /// Clears the current encounter with a controlled HP, advancing per the push
    /// policy (`bankAfter` = clear count at which to bank instead of push).
    private static func clearAll(
        _ delve: inout Delve, hpRemaining: Int, bankAfter: Int = .max
    ) {
        delve.descend()
        while !delve.isFinished {
            if delve.status == .inEncounter {
                delve.recordOutcome(petVictory: true, petHPRemaining: hpRemaining)
            } else if delve.status == .awaitingPushChoice {
                if delve.clearedCount >= bankAfter { delve.bank() } else { delve.pushDeeper() }
            }
        }
    }

    // MARK: Checks

    private static func checkStartsInBriefingThenDescends(context: inout CheckContext) {
        var delve = cacheWarren(seed: 1)
        context.expectEqual(delve.status, .briefing, "a delve opens in the briefing")
        context.expectEqual(delve.totalEncounters, 4, "Cache Warren is three encounters + boss")
        context.expectEqual(delve.currentFoe?.name, CacheWarren.mote.name, "first foe is the Mote")
        delve.descend()
        context.expectEqual(delve.status, .inEncounter, "descending begins the first encounter")
        // makeEncounter only works once descended.
        context.expect(
            cacheWarren(seed: 1).makeEncounter(approach: .aggressive) == nil,
            "no encounter is built while still in the briefing"
        )
        context.expect(
            delve.makeEncounter(approach: .aggressive) != nil,
            "an encounter is built once descended"
        )
    }

    private static func checkFullClearGrantsCompletionBonus(context: inout CheckContext) {
        var delve = cacheWarren(seed: 7)
        clearAll(&delve, hpRemaining: 80) // full HP → flawless
        context.expectEqual(delve.clearedCount, 4, "a full clear beats all four encounters")
        guard let res = delve.resolution(applyingTo: neutralState()) else {
            context.expect(false, "a completed delve resolves"); return
        }
        // 8 + 20 + 25 + 100 kill XP + 50 completion bonus.
        context.expectApproximatelyEqual(res.xpGained, 203, "full clear XP includes the completion bonus")
        context.expect(res.bossDefeated, "the boss was defeated")
        context.expect(!res.banked, "a boss clear is not a bank")
        context.expectEqual(res.tier, .flawless, "walking out at full HP is flawless")
    }

    private static func checkBankKeepsAccruedXPButNoBonus(context: inout CheckContext) {
        var delve = cacheWarren(seed: 3)
        clearAll(&delve, hpRemaining: 60, bankAfter: 2) // clear Mote + Snag, then bank
        context.expectEqual(delve.clearedCount, 2, "banking after two clears stops there")
        guard let res = delve.resolution(applyingTo: neutralState()) else {
            context.expect(false, "a banked delve resolves"); return
        }
        context.expectApproximatelyEqual(res.xpGained, 28, "banked XP is the two kills (8+20), no bonus")
        context.expect(!res.bossDefeated, "banking early never defeats the boss")
        context.expect(res.banked, "an early win exit is a bank")
    }

    private static func checkHPCarriesAndRegensOnPush(context: inout CheckContext) {
        var delve = cacheWarren(seed: 5)
        delve.descend()
        delve.recordOutcome(petVictory: true, petHPRemaining: 40)
        context.expectEqual(delve.carriedHP, 40, "HP carries out of a cleared encounter")
        context.expectEqual(delve.status, .awaitingPushChoice, "a non-boss clear pauses for bank/push")
        delve.pushDeeper()
        // Regen = round(maxHP 80 × 0.3 × effectiveness 1.0) = 24 → 40 + 24 = 64.
        context.expectEqual(delve.carriedHP, 64, "pushing regenerates 30% of max HP")
        context.expectEqual(delve.currentFoe?.name, CacheWarren.snag.name, "pushing advances to the next foe")
        let next = delve.makeEncounter(approach: .aggressive)
        context.expectEqual(next?.pet.currentHP, 64, "the next encounter starts at the carried, regenerated HP")
    }

    private static func checkDownedRetreatIsTierDownedNoXP(context: inout CheckContext) {
        var delve = cacheWarren(seed: 9)
        delve.descend()
        delve.recordOutcome(petVictory: false, petHPRemaining: 0)
        context.expectEqual(delve.status, .retreated, "losing an encounter retreats the delve")
        guard let res = delve.resolution(applyingTo: neutralState()) else {
            context.expect(false, "a retreated delve resolves"); return
        }
        context.expectEqual(res.tier, .downed, "a retreat is always the downed tier")
        context.expectApproximatelyEqual(res.xpGained, 0, "a first-encounter loss earns no XP")
        context.expect(!res.banked && !res.bossDefeated, "a retreat is neither a bank nor a boss clear")
    }

    private static func checkConditionDeltaAppliedExactlyOnce(context: inout CheckContext) {
        let before = neutralState()
        var delve = cacheWarren(seed: 11)
        clearAll(&delve, hpRemaining: 80) // flawless
        guard let res = delve.resolution(applyingTo: before) else {
            context.expect(false, "the delve resolves"); return
        }
        // Flawless moves happiness +10 and trust +5 — ONCE, not once per encounter
        // (four encounters would be +40 / +20 if the delta leaked per-fight).
        context.expectApproximatelyEqual(
            res.state.needs.happiness - before.needs.happiness, 10,
            "happiness moves by one flawless delta, not four"
        )
        context.expectApproximatelyEqual(
            res.state.needs.trust - before.needs.trust, 5,
            "trust moves by one flawless delta, not four"
        )
    }

    private static func checkEndToEndDelveReplaysIdentically(context: inout CheckContext) {
        func runReal(seed: UInt64) -> Delve {
            var delve = cacheWarren(seed: seed)
            delve.descend()
            var safety = 0
            while !delve.isFinished, safety < 64 {
                safety += 1
                switch delve.status {
                case .inEncounter:
                    guard var encounter = delve.makeEncounter(approach: .aggressive) else { break }
                    encounter.runToCompletion()
                    delve.recordOutcome(of: encounter)
                case .awaitingPushChoice:
                    delve.pushDeeper()
                case .briefing, .completed, .retreated:
                    break
                }
            }
            return delve
        }
        let a = runReal(seed: 424_242)
        let b = runReal(seed: 424_242)
        context.expectEqual(a.status, b.status, "a delve replays to the same status")
        context.expectEqual(a.carriedHP, b.carriedHP, "a delve replays to the same HP")
        context.expectApproximatelyEqual(a.accumulatedXP, b.accumulatedXP, "a delve replays to the same XP")
        context.expectEqual(a.clearedCount, b.clearedCount, "a delve replays to the same clear count")
        context.expect(a.isFinished, "the champion's delve actually finishes")
        context.expectEqual(
            a.resolution(applyingTo: neutralState()), b.resolution(applyingTo: neutralState()),
            "the write-back is identical on replay"
        )
    }
}
