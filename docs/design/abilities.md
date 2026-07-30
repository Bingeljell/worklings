# Worklings Abilities

## Status

Design direction, not implemented. Abilities are the layer directly above the
[dungeon combat model](dungeons.md): they turn the generic v1 **Signature** action into a
real, class-specific move, and they're the first thing the dungeon's reward currency buys.
This doc specs *what each class's first ability does*; **magnitudes are deliberately held
as knobs** — the point is the mechanics, tuned once it's playable.

Abilities sit on the [systems ladder](progression.md#the-systems-ladder) at step 4, gated
by level and class, unlocked by a **points currency deliberately separate from stat
growth** so the two economies never share a schema.

## The ability-point currency

- **Earned** from delves (a delve completion grants ability points — see [dungeon rewards](dungeons.md#rewards)). Never from care or work XP, so abilities are a *content* reward, not a passive drip.
- **Spent** to unlock a class's abilities. v1: one first ability per class; later, a small per-class tree in unlock order.
- **Persisted** additively on the save: a points balance + the set of unlocked ability ids. Base stats and level are untouched — this is a parallel currency, exactly as [progression](progression.md#levels) requires.
- Knobs: points granted per delve; unlock cost per ability. *(Held.)*

## How an ability works in combat

An unlocked ability becomes a choosable action at a [decision point](dungeons.md#the-players-input-strategy-at-decision-points), replacing that class's generic Signature:

- **Class- and level-gated** — you only ever see your class's abilities, past their level gate.
- **Costed in combat** — each has a use limit: a once-per-encounter charge, or a cooldown of *n* rounds. Knob: the limit per ability.
- **Scales** with the class's signature stat (the same stat the class already grows fastest), so investing in the class deepens the ability without a second number to balance.
- **Deterministic** — resolves through the same seeded PRNG as the rest of combat, so it stays testable in `CompanionCore`.

### The status-effect system these need

Several abilities (and [enemy abilities](dungeons.md#the-cache-warren)) apply **timed
buffs/debuffs** rather than instant damage — Tinkerer's mark, Aegis's riposte window,
Maverick's extra action. So abilities require a small **status-effect** primitive on the
combat model: a named effect with a magnitude and a remaining-duration, applied to a
combatant, ticked each round, and read by the formulas. This is a shared dependency worth
building once — enemy telegraphs and boss phases use it too.

## First ability per class

Each is the class's [Signature seed](dungeons.md#the-same-fight-per-class) promoted into a
costed action. Effects are qualitative here; every number is a knob.

| Class | Signature stat | First ability | What it does |
| --- | --- | --- | --- |
| **Wellspring** | Vitality | **Second Wind** | Restores a portion of max HP now, then a smaller regen-over-time for a few rounds. Turns attrition into the win condition. *(Knobs: heal %, regen amount/duration, use limit.)* |
| **Juggernaut** | Power | **Overbear** | A committed heavy strike that **cannot miss**, at a large damage multiplier — but it telegraphs (winds up a round, or drops the pet's own Guard for the round). Reliable burst with a cost. *(Knobs: damage multiplier, wind-up/Guard penalty, use limit.)* |
| **Aegis** | Guard | **Bulwark** | Braces hard this round (mitigation well above a normal Brace) **and** reflects a portion of the blocked damage back — punishing a foe that attacks into it. *(Knobs: mitigation, reflect %, use limit.)* |
| **Maverick** | Agility | **Flurry** | Takes an **extra action** this round (or a guaranteed-crit burst), exploiting speed. Best when it can end a low-HP foe outright. *(Knobs: extra hits / crit behavior, use limit.)* |
| **Tinkerer** | Wit | **Expose** | Marks the foe: a Wit-scaled debuff that **lowers its evasion and Guard** for a few rounds, making a slippery or armored target hittable for the whole party of one. The answer to the Flicker. *(Knobs: evasion/Guard reduction, duration, use limit.)* |

Read against the [Flicker walkthrough](dungeons.md#a-worked-encounter-the-flicker): Expose
neutralizes the evasion, Flurry outraces it, Overbear one-shots it, Bulwark turns its
bites back on it, Second Wind simply outlasts it. One encounter, five genuinely different
solutions — which is the whole point of building abilities against a fight that already
exists.

## Upgrade path (later)

v1 ships one ability per class. The unlock order and a small **per-class tree** (a second
and third ability, each level- and points-gated) come after the first ability loop feels
right. The currency and the unlocked-id set are designed so a tree is additive — no
reshape of the save or the combat model.

## Knobs

Held, to tune once playable: points-per-delve, unlock cost per ability, each ability's
use limit (charge vs cooldown length), and each ability's effect magnitude(s) as listed
in the table. These would live in the same `PetCombatRates` neighborhood as the dungeon
knobs.

## Open questions

1. **Signature vs first ability** — does unlocking a class's first ability *replace* the generic v1 Signature, or sit alongside it as a second choosable action?
2. **Cost model** — once-per-encounter charges (simpler) vs round cooldowns (more tactical)? Possibly per-ability.
3. **Ability scaling** — always the class's signature stat, or does Wit universally drive ability potency (making Tinkerer the "caster"), with the signature stat as a secondary?
4. **Tree shape** — how many abilities per class at the cap, and are they linear unlocks or branching?
